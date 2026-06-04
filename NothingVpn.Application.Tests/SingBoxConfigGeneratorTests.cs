using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Application.Tests;

public sealed class SingBoxConfigGeneratorTests
{
    [Theory]
    [InlineData("tun")]
    [InlineData("tun_apps")]
    public void Build_TunModes_DefaultDomainResolverMatchesDnsServerTag(string mode)
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "testprof",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = mode,
            DnsMode = mode == "tun" ? "doh" : "doh",
            TunAppProcessPaths = mode == "tun_apps"
                ? ["C:\\Apps\\firefox.exe"]
                : []
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        var resolver = config.Route.DefaultDomainResolver;
        Assert.False(string.IsNullOrWhiteSpace(resolver));

        var tags = config.Dns!.Servers.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(resolver, tags);
    }

    [Fact]
    public void Build_TunApps_SplitTunnelRoutePolicy()
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "testprof",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = "tun_apps",
            DnsMode = "doh",
            TunStrictRoute = true,
            TunAppProcessPaths = ["C:\\Apps\\firefox.exe"]
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        var rules = config.Route.Rules ?? [];
        var inbounds = config.Inbounds ?? [];

        Assert.Contains(inbounds, i => string.Equals(i.Type, "tun", StringComparison.Ordinal));
        Assert.DoesNotContain(inbounds, i => i.StrictRoute == true);
        Assert.Contains(rules, r =>
            string.Equals(r.Protocol, "dns", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Action, "hijack-dns", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            r.Domain is not null
            && r.Domain.Contains("node.example", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "direct", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            r.Domain is not null
            && r.Domain.Contains("dns.google", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            r.ProcessPath is not null
            && r.ProcessPath.Contains("C:\\Apps\\firefox.exe", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Equal("direct", config.Route.Final);
        Assert.Equal("ipv4_only", config.Dns!.Strategy);
    }

    [Fact]
    public void Build_FullTun_RoutesIpv6AndQuicThroughProxy()
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "tunfull",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState { Mode = "tun", DnsMode = "doh", TunStrictRoute = true };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        var rules = config.Route.Rules ?? [];

        Assert.Contains(rules, r => r.IpVersion == 6 && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            string.Equals(r.Protocol, "quic", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Equal("proxy", config.Route.Final);
        Assert.Equal("ipv4_only", config.Dns!.Strategy);
    }

    [Fact]
    public void Build_ProxyMode_MixedInboundAndNoDnsBlock()
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "proxy1",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = "proxy",
            LocalMixedPort = 2080,
            DnsMode = "system"
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        Assert.Null(config.Dns);
        Assert.Equal("proxy", config.Route.Final);
        var mixed = Assert.Single(config.Inbounds);
        Assert.Equal("mixed", mixed.Type);
        Assert.Equal(2080, mixed.ListenPort);
        Assert.Equal("127.0.0.1", mixed.Listen);
    }
}
