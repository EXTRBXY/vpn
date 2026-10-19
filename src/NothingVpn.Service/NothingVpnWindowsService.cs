using System.Diagnostics;
using System.ServiceProcess;
using NothingVpn.Infrastructure.ServiceHost;

namespace NothingVpn.Service;

/// <summary>
/// Служба Windows «NothingVpn» (LocalSystem, ADR-0001): ядро sing-box и канал <c>NothingVpn.Service</c>.
/// Регистрирует службу установщик (шаг 7б); в 7а её можно поставить вручную через sc.exe.
/// </summary>
internal sealed class NothingVpnWindowsService : ServiceBase
{
    public const string Name = "NothingVpn";

    private ServiceEngineHost? _host;

    public NothingVpnWindowsService()
    {
        ServiceName = Name;
        CanStop = true;
        CanShutdown = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            _host = ServiceEngineHost.Create(AppContext.BaseDirectory);
            _host.Start();
        }
        catch (Exception ex)
        {
            // Например, имя канала уже занято чужим процессом: служба не работает поверх чужого канала.
            EventLog.WriteEntry($"Служба Nothing VPN не запустилась: {ex.Message}", EventLogEntryType.Error);
            StopHost();
            throw;
        }
    }

    protected override void OnStop() => StopHost();

    protected override void OnShutdown() => StopHost();

    private void StopHost()
    {
        var host = Interlocked.Exchange(ref _host, null);
        if (host is null)
            return;

        // Остановка ядра ограничена тайм-аутами раннера (2 с), диспетчер служб ждёт дольше.
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
