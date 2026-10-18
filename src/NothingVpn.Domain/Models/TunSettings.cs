using NothingVpn.Domain.Policies;

namespace NothingVpn.Domain.Models;

public sealed class TunSettings
{
    public string InterfaceName { get; set; } = TunSettingsPolicy.DefaultInterfaceName;
    public string AddressCidr { get; set; } = TunSettingsPolicy.AutoAddressCidr;
    public int Mtu { get; set; } = TunSettingsPolicy.DefaultMtu;
    public string Stack { get; set; } = TunSettingsPolicy.DefaultStack;
    public bool AutoRoute { get; set; } = true;
    public bool StrictRoute { get; set; } = true;
}
