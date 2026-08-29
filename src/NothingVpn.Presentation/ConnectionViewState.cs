namespace NothingVpn.Presentation;

public sealed record ConnectionViewState(
    bool IsRunning,
    bool CanStart,
    bool CanStop,
    bool CanEditConnection,
    bool CanEditTunApps,
    string WindowTitle,
    string StatusText,
    string AdministratorText,
    string ModeText,
    string ProfileText,
    string PortText,
    string DnsText,
    string RuleSetsText,
    string TunText,
    string ProxyBypassText);
