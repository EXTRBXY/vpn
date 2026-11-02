using System.Text.Json;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Windows;

namespace NothingVpn.Infrastructure.SingBox;

internal sealed class SingBoxRunner : IDisposable
{
    private const string ExeName = "sing-box.exe";
    private static readonly TimeSpan ProcessStopTimeout = TimeSpan.FromSeconds(2);

    private readonly string _singBoxExePathHint;
    private readonly string? _installDirectory;
    private readonly SupervisedProcess _process;
    private readonly object _gate = new();
    private string? _lastConfigPath;

    public event EventHandler? ProcessExited;

    public bool IsRunning => _process.IsRunning;

    public SingBoxRunner(
        string singBoxExePath,
        InMemoryLogStore logStore)
    {
        _singBoxExePathHint = singBoxExePath;
        _process = new SupervisedProcess("sing-box", logStore);
        _process.Exited += (_, _) =>
        {
            TryDeleteLastConfig();
            ProcessExited?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Режим службы (ADR-0001): sing-box.exe и wintun.dll берутся только из каталога установки службы —
    /// без поиска по bin\, родительским каталогам и пользовательской установке.
    /// </summary>
    public static SingBoxRunner ForInstallDirectory(string installDirectory, InMemoryLogStore logStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        var directory = Path.GetFullPath(installDirectory);
        return new SingBoxRunner(Path.Combine(directory, ExeName), logStore, directory);
    }

    private SingBoxRunner(string singBoxExePath, InMemoryLogStore logStore, string installDirectory)
        : this(singBoxExePath, logStore)
    {
        _installDirectory = installDirectory;
    }

    public void ValidateConfig(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            throw new FileNotFoundException("sing-box config not found.", configPath);

        var psi = ChildProcess.CreateStartInfo(ResolveExePathOrThrow(), $"check -c \"{configPath}\"");
        var (exitCode, stdout, stderr) = ChildProcess.RunAsync(psi, ProcessStopTimeout)
            .GetAwaiter()
            .GetResult();

        if (exitCode == 0)
            return;

        var detail = ChildProcess.ExtractFailure(stdout, stderr);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail)
                ? $"sing-box check failed (exit {exitCode})."
                : $"sing-box check failed: {detail}");
    }

    public void Start(string configPath)
    {
        if (UsesTunInbound(configPath) && IsRunning)
            Stop();

        var startInfo = ChildProcess.CreateStartInfo(ResolveExePathOrThrow(), $"run -c \"{configPath}\"");
        lock (_gate)
        {
            _lastConfigPath = configPath;
        }
        _process.Start(startInfo);
    }

    public void TryDeleteLastConfig()
    {
        string? path;
        lock (_gate)
        {
            path = _lastConfigPath;
            _lastConfigPath = null;
        }

        if (string.IsNullOrWhiteSpace(path)) return;
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

    public void Stop()
    {
        try
        {
            _process.Stop(ProcessStopTimeout);
        }
        finally
        {
            TryDeleteLastConfig();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static bool UsesTunInbound(string configPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("inbounds", out var inbounds))
                return false;

            foreach (var inbound in inbounds.EnumerateArray())
            {
                if (inbound.TryGetProperty("type", out var typeProp)
                    && string.Equals(typeProp.GetString(), "tun", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // best-effort
        }

        return false;
    }

    private string ResolveExePathOrThrow()
    {
        if (_installDirectory is not null)
        {
            return ResolveInInstallDirectory(_installDirectory)
                ?? throw new FileNotFoundException(
                    $"sing-box.exe не найден в каталоге службы: {_installDirectory}",
                    _singBoxExePathHint);
        }

        // TUN на Windows грузит wintun.dll из каталога sing-box (WorkingDirectory).
        // Предпочитаем полный комплект, чтобы publish без wintun не перекрывал установку.
        return ExecutableLocator.Find(
                ExeName,
                _singBoxExePathHint,
                path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "wintun.dll")))
            ?? throw new FileNotFoundException(
                "sing-box.exe not found. Put it next to the app or into a ./bin folder near it.",
                _singBoxExePathHint);
    }

    internal static string? ResolveInInstallDirectory(string installDirectory) =>
        ExecutableLocator.InInstallDirectory(installDirectory, ExeName);
}
