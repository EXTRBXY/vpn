namespace NothingVpn.Domain.Models;

public sealed class ProxyConnectionSettings
{
    public string ProxyOverride { get; set; } = "localhost;127.*;10.*;192.168.*;172.16.*";
}
