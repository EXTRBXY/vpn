using NothingVpn.Application.Engine;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Infrastructure.ServiceChannel;
using NothingVpn.Infrastructure.SingBox;

namespace NothingVpn.Infrastructure.ServiceHost;

/// <summary>
/// Composition root службы (<c>NothingVpn.Service</c>): то же ядро <see cref="SingBoxVpnEngine"/>, что внутри
/// клиента, но с портами службы — sing-box только из каталога установки, конфиги и rule-set в
/// <c>%ProgramData%\NothingVpn</c>, журнал в памяти службы (маскируется при записи) — и сервер канала поверх.
/// </summary>
public sealed class ServiceEngineHost : IAsyncDisposable
{
    private readonly SingBoxRunner _runner;
    private readonly ServicePipeServer _server;

    private ServiceEngineHost(SingBoxVpnEngine engine, InMemoryLogStore logStore, SingBoxRunner runner, ServicePipeServer server)
    {
        Engine = engine;
        LogStore = logStore;
        _runner = runner;
        _server = server;
    }

    public IVpnEngine Engine { get; }
    public InMemoryLogStore LogStore { get; }

    /// <param name="installDirectory">Каталог exe службы; sing-box.exe и wintun.dll ищутся только здесь.</param>
    /// <param name="dataDirectory">Каталог данных; по умолчанию <c>%ProgramData%\NothingVpn</c> с ACL SYSTEM и администраторов.</param>
    public static ServiceEngineHost Create(string installDirectory, string? dataDirectory = null, ServicePipeServerOptions? pipeOptions = null)
    {
        var paths = ServiceDataDirectory.Create(dataDirectory ?? ServiceDataDirectory.DefaultRoot, applyAcl: dataDirectory is null);
        var logStore = new InMemoryLogStore(maxBytes: 1_000_000);
        var runner = SingBoxRunner.ForInstallDirectory(installDirectory, logStore);
        var runtime = new LegacyRuntimeContext(paths, logStore, runner);
        var engine = new SingBoxVpnEngine(
            new SingBoxPort(runtime),
            new DiagnosticsPort(),
            new PathPolicyPort(),
            new LogPort(logStore),
            new ServiceRuleSetStore(paths.RuleSetsDir));

        var options = pipeOptions ?? new ServicePipeServerOptions
        {
            Trace = message => logStore.AppendStructured(LogLevel.Error, "service", message)
        };
        return new ServiceEngineHost(engine, logStore, runner, new ServicePipeServer(engine, logStore, options));
    }

    /// <summary>Занимает канал службы. Исключение — имя канала уже занято (другим экземпляром или чужим процессом).</summary>
    public void Start() => _server.Start();

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        try { await Engine.DisconnectAsync().ConfigureAwait(false); }
        catch { }
        _runner.Dispose();
    }
}
