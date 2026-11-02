using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Security;
using NothingVpn.Infrastructure.Windows;
using NothingVpn.Infrastructure.Xray;

namespace NothingVpn.Infrastructure.Ports;

/// <summary>
/// Xray как выход к серверу (ADR-0002). Конфиг с одноразовыми учётными данными SOCKS5 пишется в каталог конфигов
/// ядра, проверяется <c>xray run -test</c> и удаляется при остановке или выходе процесса.
/// </summary>
public sealed class XrayOutboundPort : IOutboundEnginePort, IDisposable
{
    private const string ExeName = "xray.exe";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(5);

    private readonly string _configsDirectory;
    private readonly string? _installDirectory;
    private readonly SupervisedProcess _process;
    private readonly object _gate = new();
    private string? _configPath;

    /// <param name="installDirectory">Служба: xray.exe только из каталога установки. null — поиск рядом с клиентом.</param>
    internal XrayOutboundPort(string configsDirectory, InMemoryLogStore logStore, string? installDirectory = null)
    {
        _configsDirectory = configsDirectory;
        _installDirectory = installDirectory is null ? null : Path.GetFullPath(installDirectory);
        _process = new SupervisedProcess("xray", logStore);
        _process.Exited += (_, _) =>
        {
            DeleteConfig();
            ProcessExited?.Invoke(this, EventArgs.Empty);
        };
    }

    public OutboundEngine Engine => OutboundEngine.Xray;

    public event EventHandler? ProcessExited;

    public bool IsRunning => _process.IsRunning;

    public LocalSocksUpstream Start(VpnProfile profile, AppStateModel state)
    {
        var exePath = ResolveExePathOrThrow();
        var upstream = new LocalSocksUpstream(
            FindFreeLoopbackPort(),
            RandomNumberGenerator.GetHexString(32, lowercase: true),
            RandomNumberGenerator.GetHexString(32, lowercase: true),
            exePath);

        Directory.CreateDirectory(_configsDirectory);
        var configPath = Path.Combine(_configsDirectory, $"{profile.Id}.xray.json");
        lock (_gate)
        {
            _configPath = configPath;
        }
        File.WriteAllText(configPath, XrayConfigGenerator.Serialize(profile, state, upstream));

        Test(exePath, configPath);
        _process.Start(ChildProcess.CreateStartInfo(exePath, $"run -c \"{configPath}\""));
        return upstream;
    }

    public void Stop()
    {
        try
        {
            _process.Stop(ProcessTimeout);
        }
        finally
        {
            DeleteConfig();
        }
    }

    public void Dispose() => Stop();

    private static void Test(string exePath, string configPath)
    {
        var psi = ChildProcess.CreateStartInfo(exePath, $"run -test -c \"{configPath}\"");
        var (exitCode, stdout, stderr) = ChildProcess.RunAsync(psi, ProcessTimeout).GetAwaiter().GetResult();
        if (exitCode == 0)
            return;

        var detail = LogRedactor.Redact(ChildProcess.ExtractFailure(stdout, stderr));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Xray отклонил конфигурацию (код {exitCode})."
                : $"Xray отклонил конфигурацию: {detail}");
    }

    private void DeleteConfig()
    {
        string? path;
        lock (_gate)
        {
            path = _configPath;
            _configPath = null;
        }

        if (path is null)
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort; do not fail disconnect
        }
    }

    private string ResolveExePathOrThrow()
    {
        if (_installDirectory is not null)
        {
            return ExecutableLocator.InInstallDirectory(_installDirectory, ExeName)
                ?? throw new FileNotFoundException($"xray.exe не найден в каталоге службы: {_installDirectory}", ExeName);
        }

        return ExecutableLocator.Find(ExeName, hint: null)
            ?? throw new FileNotFoundException("xray.exe не найден. Положите его рядом с приложением или в папку bin.", ExeName);
    }

    private static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
