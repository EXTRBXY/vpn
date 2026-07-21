using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface ISingBoxPort
{
    event EventHandler? ProcessExited;
    bool IsRunning { get; }
    string WriteConfig(VpnProfile profile, AppStateModel state);
    void Start(string configPath);
    void Stop();
    void TryDeleteLastConfig();
}
