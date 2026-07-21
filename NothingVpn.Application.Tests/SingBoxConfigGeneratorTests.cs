using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Store;

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
        Assert.Equal("hijack-dns", config.Route.Rules[1].Action);
        Assert.Equal("reject", config.Route.Rules[2].Action);
        Assert.Contains("stun", config.Route.Rules[2].Protocol!);
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
        Assert.Contains(config.Dns.Servers, s => s.Tag == "local");
        Assert.Contains(config.Dns.Servers, s => s.Tag == "doh");
        Assert.NotNull(config.Dns.Rules);
        Assert.Contains(config.Dns.Rules!, r =>
            r.Server == "local" &&
            r.RuleSet != null &&
            r.RuleSet.Contains("geosite-category-ru"));
        Assert.DoesNotContain(config.Dns.Rules!, r =>
            r.RuleSet != null && r.RuleSet.Contains("geoip-ru"));
    }

    [Fact]
    public void Build_ProxyWithoutRuleSets_HasNoSniff()
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
        Assert.True(config.Route.Rules is null || config.Route.Rules.Count == 0);
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
        Assert.Contains(config.Dns!.Servers, s => s.Tag == "local");
        var doh = Assert.Single(config.Dns.Servers, s => s.Tag == "doh");
        Assert.Equal("proxy", doh.Detour);
        Assert.Equal("doh", config.Route.DefaultDomainResolver);
        var proxy = Assert.Single(config.Outbounds, o => o.Tag == "proxy");
        Assert.Equal("local", proxy.DomainResolver);
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
    public void UpdateChannelOptions_AcceptsTempInstallerName()
    {
        Assert.True(NothingVpn.Tray.Internal.Updates.UpdateChannelOptions.IsAcceptedInstallerFileName("NothingVpnSetup.exe"));
        Assert.True(NothingVpn.Tray.Internal.Updates.UpdateChannelOptions.IsAcceptedInstallerFileName(@"C:\Temp\NothingVpnSetup-1.2.3.exe"));
        Assert.False(NothingVpn.Tray.Internal.Updates.UpdateChannelOptions.IsAcceptedInstallerFileName("malware.exe"));
    }

    [Fact]
    public void BuiltinCatalog_IncludesGeoIpRu()
    {
        var def = NothingVpn.Tray.Internal.RuleSets.BuiltinGeositeRuleSets.FindByBuiltinId("sing-geoip:ru");
        Assert.NotNull(def);
        Assert.Equal("geoip-ru.srs", def!.FileName);
        Assert.Equal("geoip-ru", def.RouteTag);
    }
}
