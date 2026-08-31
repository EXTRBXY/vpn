using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

public sealed class SecurityRegressionTests
{
    [Theory]
    [InlineData("zz&security", "none")]
    [InlineData("zz&SeCuRiTy", "none")]
    [InlineData("zz=other", "value")]
    [InlineData("zz#fragment", "value")]
    [InlineData("zz%26security", "none")]
    [InlineData("параметр", "значение & = #")]
    [InlineData("ech", "ordinary-value")]
    public void VlessRoundTrip_PreservesUnknownKeysWithoutChangingTls(string key, string value)
    {
        var original = VlessLinkParser.Parse(
            "vless://11111111-1111-1111-1111-111111111111@node.example:443?" +
            $"security=tls&type=tcp&sni=cover.example&{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}#Name");
        var current = original;
        for (var i = 0; i < 3; i++)
        {
            current = VlessLinkParser.Parse(VlessLinkFormatter.Build(LegacyModelMapper.ToModel(current)));
            Assert.Equal(original.Security, current.Security);
            Assert.Equal(original.Type, current.Type);
            Assert.Equal(original.Sni, current.Sni);
            Assert.Equal(original.Name, current.Name);
            Assert.Equal(original.Id, current.Id);
            Assert.Equal(value, current.ExtraQuery[key]);
            Assert.Single(current.ExtraQuery);
        }
    }

    [Theory]
    [InlineData("tun", "doh", "node.example")]
    [InlineData("tun_apps", "doh", "node.example")]
    [InlineData("tun", "system", "node.example")]
    [InlineData("tun_apps", "system", "node.example")]
    [InlineData("tun", "doh", "203.0.113.7")]
    [InlineData("tun_apps", "doh", "2001:db8::7")]
    public void TunBootstrap_DoesNotGrantSniDirectRoutingOrLocalDns(string mode, string dnsMode, string host)
    {
        var paths = new AppPaths(".", ".", ".", ".", "profiles.json", "subscriptions.json", "state.json");
        var profile = new VlessProfile
        {
            Host = host, Sni = "cover.example", Port = 443,
            Uuid = "11111111-1111-1111-1111-111111111111", Security = "tls", Type = "tcp"
        };
        var state = new AppState
        {
            Mode = mode, DnsMode = dnsMode, DohSni = "dns.google",
            TunAppProcessPaths = [@"C:\Apps\browser.exe"]
        };
        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        Assert.DoesNotContain(config.Route.Rules!, r => r.Outbound == "direct" && r.Domain?.Contains(profile.Sni) == true);
        Assert.DoesNotContain(config.Dns!.Rules ?? [], r => r.Domain?.Contains(profile.Sni) == true);
        Assert.Equal(profile.Sni, config.Outbounds.Single(o => o.Tag == "proxy").Tls!.ServerName);
        if (host == "node.example")
        {
            Assert.Contains(config.Route.Rules!, r => r.Outbound == "direct" && r.Domain?.Contains(host) == true);
            Assert.Contains(config.Dns.Rules!, r => r.Server == "bootstrap-local" && r.Domain?.Contains(host) == true);
        }
    }
}
