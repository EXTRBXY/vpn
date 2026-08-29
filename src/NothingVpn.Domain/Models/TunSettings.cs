namespace NothingVpn.Domain.Models;

public sealed class TunSettings
{
    public string InterfaceName { get; set; } = "NothingVpn";
    public string AddressCidr { get; set; } = "auto";
    public int Mtu { get; set; } = 1500;
    public string Stack { get; set; } = "";
    public bool AutoRoute { get; set; } = true;
    public bool StrictRoute { get; set; } = true;
}
