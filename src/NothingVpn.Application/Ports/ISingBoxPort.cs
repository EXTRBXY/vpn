using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface ISingBoxPort
{
    event EventHandler? ProcessExited;
    bool IsRunning { get; }

    /// <param name="upstream">Если задан, выход «proxy» — SOCKS5 внешнего движка, а не VLESS (ADR-0002).</param>
    string WriteConfig(VpnProfile profile, AppStateModel state, LocalSocksUpstream? upstream = null);
    void ValidateConfig(string configPath);
    void Start(string configPath);
    void Stop();
    void TryDeleteLastConfig();
}
