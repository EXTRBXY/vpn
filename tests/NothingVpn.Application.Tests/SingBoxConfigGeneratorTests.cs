using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

public sealed class SingBoxConfigGeneratorTests
{
    [Fact]
    public void Build_TunWithRuDirectAndDoh_HasSniffSplitDnsAndGeoip()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var ruleSetsDir = Path.Combine(baseDir, "rulesets");
        Directory.CreateDirectory(ruleSetsDir);
        File.WriteAllBytes(Path.Combine(ruleSetsDir, "geosite-category-ru.srs"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(ruleSetsDir, "geoip-ru.srs"), new byte[] { 1 });

        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: ruleSetsDir,
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "abcdef123456",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "tun",
            DnsMode = "doh",
            DohServer = "8.8.8.8",
            DohPath = "/dns-query",
            DohSni = "dns.google",
            DnsDetour = "direct",
            UserRuleSets =
            [
                new UserRuleSet
                {
                    Tag = "geosite-category-ru",
                    Name = "RU",
                    FileName = "geosite-category-ru.srs",
                    Enabled = true,
                    Action = "direct",
                    BuiltinId = "sing-geosite:category-ru"
                },
                new UserRuleSet
                {
                    Tag = "geoip-ru",
                    Name = "GeoIP RU",
                    FileName = "geoip-ru.srs",
                    Enabled = true,
                    Action = "direct",
                    BuiltinId = "sing-geoip:ru"
                }
            ]
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        Assert.NotNull(config.Route.Rules);
        Assert.Equal("sniff", config.Route.Rules![0].Action);
        Assert.Contains(config.Route.Rules, r => r.Action == "hijack-dns");
        Assert.Contains(config.Route.Rules, r =>
            r.Action == "reject" &&
            string.Equals(r.Protocol, "stun", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(config.Route.Rules, r =>
            r.Outbound == "direct" &&
            r.RuleSet != null &&
            r.RuleSet.Contains("geosite-category-ru"));
        Assert.Contains(config.Route.Rules, r =>
            r.Outbound == "direct" &&
            r.RuleSet != null &&
            r.RuleSet.Contains("geoip-ru"));
        Assert.Contains(config.Route.RuleSet!, rs => rs.Tag == "geoip-ru");

        Assert.NotNull(config.Inbounds[0].Address);
        Assert.Equal(2, config.Inbounds[0].Address!.Count);
        Assert.Contains(config.Inbounds[0].Address!, a => a.Contains(':', StringComparison.Ordinal));

        Assert.NotNull(config.Dns);
        Assert.Equal("doh", config.Dns!.Final);
        Assert.Contains(config.Dns.Servers, s => s.Tag == "bootstrap-local");
        Assert.Contains(config.Dns.Servers, s => s.Tag == "doh");
        Assert.NotNull(config.Dns.Rules);
        Assert.Contains(config.Dns.Rules!, r =>
            r.Server == "bootstrap-local" &&
            r.RuleSet != null &&
            r.RuleSet.Contains("geosite-category-ru"));
        Assert.DoesNotContain(config.Dns.Rules!, r =>
            r.RuleSet != null && r.RuleSet.Contains("geoip-ru"));
    }

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
    public void Build_ProxyWithoutRuleSets_HasSniffAndStunReject()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: Path.Combine(baseDir, "rulesets"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "proxyprofile",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "proxy",
            DnsMode = "system",
            UserRuleSets = []
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        Assert.NotNull(config.Route.Rules);
        Assert.Contains(config.Route.Rules!, r => r.Action == "sniff");
        Assert.Contains(config.Route.Rules!, r =>
            r.Action == "reject" &&
            string.Equals(r.Protocol, "stun", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ProxyWithRuleSets_HasSniffAndStunReject()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var ruleSetsDir = Path.Combine(baseDir, "rulesets");
        Directory.CreateDirectory(ruleSetsDir);
        File.WriteAllBytes(Path.Combine(ruleSetsDir, "geosite-category-ru.srs"), new byte[] { 1 });

        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: ruleSetsDir,
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "proxyrules",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "proxy",
            DnsMode = "system",
            UserRuleSets =
            [
                new UserRuleSet
                {
                    Tag = "geosite-category-ru",
                    FileName = "geosite-category-ru.srs",
                    Enabled = true,
                    Action = "direct"
                }
            ]
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        Assert.NotNull(config.Route.Rules);
        Assert.Contains(config.Route.Rules!, r => r.Action == "sniff");
        Assert.Contains(config.Route.Rules!, r =>
            r.Action == "reject" &&
            r.Protocol != null &&
            r.Protocol.Contains("stun"));
    }

    [Fact]
    public void Build_ProxyDohWithDetourProxy_SetsDnsDetourBootstrapLocalAndDomainResolver()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: Path.Combine(baseDir, "rulesets"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "proxydoh",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "proxy",
            DnsMode = "doh",
            DohServer = "1.1.1.1",
            DohPath = "/dns-query",
            DohSni = "cloudflare-dns.com",
            DnsDetour = "proxy",
            UserRuleSets = []
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        Assert.NotNull(config.Dns);
        Assert.Contains(config.Dns!.Servers, s => s.Tag == "bootstrap-local");
        var doh = Assert.Single(config.Dns.Servers, s => s.Tag == "doh");
        Assert.Equal("proxy", doh.Detour);
        Assert.Equal("doh", config.Route.DefaultDomainResolver);
        var proxy = Assert.Single(config.Outbounds, o => o.Tag == "proxy");
        Assert.Equal("bootstrap-local", proxy.DomainResolver);
    }

    [Fact]
    public void Build_TunAppsDohWithDetourProxy_ForcesDirectDnsDetour()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: Path.Combine(baseDir, "rulesets"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "tunappsdoh",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "tun_apps",
            DnsMode = "doh",
            DohServer = "1.1.1.1",
            DohPath = "/dns-query",
            DohSni = "cloudflare-dns.com",
            DnsDetour = "proxy",
            TunAppProcessPaths = [@"C:\Windows\System32\notepad.exe"],
            UserRuleSets = []
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        Assert.NotNull(config.Dns);
        var doh = Assert.Single(config.Dns!.Servers, s => s.Tag == "doh");
        Assert.Null(doh.Detour);
        var proxy = Assert.Single(config.Outbounds, o => o.Tag == "proxy");
        Assert.Null(proxy.DomainResolver);
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
        Assert.Contains(rules, r => string.Equals(r.Action, "hijack-dns", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            r.Domain is not null
            && r.Domain.Contains("node.example", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "direct", StringComparison.Ordinal));
        Assert.DoesNotContain(rules, r =>
            r.Domain is not null
            && r.Domain.Contains("dns.google", StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(rules, r =>
            string.Equals(r.Protocol, "quic", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rules, r =>
            r.ProcessPath is not null
            && r.ProcessPath.Contains("C:\\Apps\\firefox.exe", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Contains(rules, r =>
            r.ProcessName is not null
            && r.ProcessName.Contains("firefox.exe", StringComparer.OrdinalIgnoreCase)
            && string.Equals(r.Outbound, "proxy", StringComparison.Ordinal));
        Assert.Equal("direct", config.Route.Final);
        Assert.Equal("ipv4_only", config.Dns!.Strategy);
    }

    [Fact]
    public void Build_TunApps_UserRuleSetsBeforeProcessPath()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var ruleSetsDir = Path.Combine(baseDir, "rulesets");
        Directory.CreateDirectory(ruleSetsDir);
        File.WriteAllBytes(Path.Combine(ruleSetsDir, "geosite-category-ru.srs"), new byte[] { 1 });

        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: ruleSetsDir,
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));

        var profile = new VlessProfile
        {
            Id = "tunappsrules",
            Host = "example.com",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Type = "tcp",
            Sni = "example.com"
        };

        var state = new AppState
        {
            Mode = "tun_apps",
            DnsMode = "system",
            TunAppProcessPaths = [@"C:\Apps\browser.exe"],
            UserRuleSets =
            [
                new UserRuleSet
                {
                    Tag = "geosite-category-ru",
                    FileName = "geosite-category-ru.srs",
                    Enabled = true,
                    Action = "direct"
                }
            ]
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        var rules = config.Route.Rules!;
        var ruleSetIndex = rules.FindIndex(r =>
            r.RuleSet != null && r.RuleSet.Contains("geosite-category-ru"));
        var processIndex = rules.FindIndex(r => r.ProcessPath is { Count: > 0 });
        Assert.True(ruleSetIndex >= 0);
        Assert.True(processIndex >= 0);
        Assert.True(ruleSetIndex < processIndex);
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
    public void Build_FullTun_UserRuleSetsTakePriorityOverIpv6AndQuicFallbacks()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "NothingVpnTests", Guid.NewGuid().ToString("N"));
        var ruleSetsDir = Path.Combine(baseDir, "rulesets");
        Directory.CreateDirectory(ruleSetsDir);
        File.WriteAllBytes(Path.Combine(ruleSetsDir, "marketplaces.srs"), new byte[] { 1 });

        var paths = new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: ruleSetsDir,
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: Path.Combine(baseDir, "profiles.json"),
            SubscriptionsJsonPath: Path.Combine(baseDir, "subscriptions.json"),
            StateJsonPath: Path.Combine(baseDir, "state.json"));
        var profile = new VlessProfile
        {
            Id = "tunrulespriority",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = "tun",
            DnsMode = "system",
            UserRuleSets =
            [
                new UserRuleSet
                {
                    Tag = "marketplaces",
                    FileName = "marketplaces.srs",
                    Enabled = true,
                    Action = "direct"
                }
            ]
        };

        var rules = SingBoxConfigGenerator.Build(paths, profile, state).Route.Rules!;
        var ruleSetIndex = rules.FindIndex(r => r.RuleSet?.Contains("marketplaces") == true);
        var quicIndex = rules.FindIndex(r => string.Equals(r.Protocol, "quic", StringComparison.OrdinalIgnoreCase));
        var ipv6Index = rules.FindIndex(r => r.IpVersion == 6);

        Assert.True(ruleSetIndex >= 0);
        Assert.True(quicIndex > ruleSetIndex);
        Assert.True(ipv6Index > ruleSetIndex);
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

    [Fact]
    public void Build_TunMode_CustomMtuStackAndStrictRoute()
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "tuncustom",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = "tun",
            DnsMode = "doh",
            TunMtu = 1400,
            TunStack = "gvisor",
            TunStrictRoute = false,
            TunInterfaceName = "CustomVpn",
            TunAddressCidr = "198.18.50.1/30"
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        var tun = Assert.Single(config.Inbounds, i => i.Type == "tun");

        Assert.Equal(1400, tun.Mtu);
        Assert.Equal("gvisor", tun.Stack);
        Assert.False(tun.StrictRoute);
        Assert.NotNull(tun.Address);
        Assert.Contains("198.18.50.1/30", tun.Address!);
        Assert.Contains(tun.Address!, a => a.Contains(':', StringComparison.Ordinal));
        Assert.StartsWith("CustomVpn-", tun.InterfaceName, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltinCatalog_IncludesGeoIpRu()
    {
        var def = NothingVpn.Infrastructure.RuleSets.BuiltinGeositeRuleSets.FindByBuiltinId("sing-geoip:ru");
        Assert.NotNull(def);
        Assert.Equal("geoip-ru.srs", def!.FileName);
        Assert.Equal("geoip-ru", def.RouteTag);
    }
}
