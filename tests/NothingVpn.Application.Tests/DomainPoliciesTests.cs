using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Domain.Subscriptions;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Application.Tests;

public sealed class DomainPoliciesTests
{
    [Theory]
    [InlineData("proxy", "proxy")]
    [InlineData("tun", "tun")]
    [InlineData("tun_apps", "tun_apps")]
    [InlineData("unknown", "proxy")]
    public void ParseMode_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, Mode(input).ToValue());
    }

    [Fact]
    public void EnsureTunAppsHasTargets_ThrowsWhenEmpty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectionPolicy.EnsureTunAppsHasTargets(ConnectionMode.TunApps, Array.Empty<string>()));
        Assert.Contains("TUN", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DnsPolicy_Normalize_FillsDefaults()
    {
        var settings = new DnsSettings
        {
            Mode = (DnsMode)42,
            Detour = (DnsDetour)42,
            DohPath = ""
        };

        DnsPolicy.Normalize(settings);

        Assert.Equal("doh", settings.Mode.ToValue());
        Assert.Equal("direct", settings.Detour.ToValue());
        Assert.Equal("/dns-query", settings.DohPath);
    }
    [Theory]
    [InlineData("tun", "proxy", "proxy")]
    [InlineData("proxy", "proxy", "proxy")]
    [InlineData("tun_apps", "proxy", "direct")]
    [InlineData("tun", "direct", "direct")]
    public void DnsDetourPolicy_EffectiveDetour_RespectsTunApps(string mode, string requested, string expected)
    {
        Assert.Equal(expected, DnsDetourPolicy.EffectiveDetour(Mode(mode), Detour(requested)).ToValue());
    }

    [Fact]
    public void DnsPolicy_Validate_RequiresSniForDoh()
    {
        var ok = new DnsSettings
        {
            Mode = DnsMode.Doh,
            DohServer = "8.8.8.8",
            DohSni = "dns.google",
            DohPath = "/dns-query"
        };
        DnsPolicy.Validate(ok);

        var bad = new DnsSettings
        {
            Mode = DnsMode.Doh,
            DohServer = "8.8.8.8",
            DohSni = "",
            DohPath = "/dns-query"
        };
        Assert.Throws<InvalidOperationException>(() => DnsPolicy.Validate(bad));
    }

    [Theory]
    [InlineData("1.1.1.1", "cloudflare-dns.com", "/dns-query", DnsPreset.Cloudflare)]
    [InlineData("8.8.8.8", "dns.google", "/dns-query", DnsPreset.Google)]
    [InlineData("9.9.9.9", "dns.quad9.net", "/dns-query", DnsPreset.Quad9)]
    [InlineData("94.140.14.14", "dns.adguard.com", "/dns-query", DnsPreset.AdGuard)]
    [InlineData("8.8.8.8", "dns.google", "/custom", DnsPreset.Custom)]
    public void DnsPolicy_DetectPreset_ReflectsActualDnsFields(
        string server, string sni, string path, DnsPreset expected)
    {
        var settings = new DnsSettings { DohServer = server, DohSni = sni, DohPath = path };

        Assert.Equal(expected, DnsPolicy.DetectPreset(settings));
    }

    [Fact]
    public void RuleSetRoutingPolicy_TunAlwaysRequiresSniff()
    {
        var decision = RuleSetRoutingPolicy.Evaluate(Array.Empty<UserRuleSetEntry>(), DnsMode.System, isTunMode: true);
        Assert.True(decision.RequiresSniff);
        Assert.False(decision.RequiresSplitDns);
        Assert.Empty(decision.DirectRuleSetTags);
    }

    [Fact]
    public void RuleSetRoutingPolicy_ProxyRequiresSniffOnlyWithEnabledRuleSets()
    {
        var empty = RuleSetRoutingPolicy.Evaluate(Array.Empty<UserRuleSetEntry>(), DnsMode.Doh, isTunMode: false);
        Assert.False(empty.RequiresSniff);

        var withDirect = RuleSetRoutingPolicy.Evaluate(
            new[]
            {
                new UserRuleSetEntry
                {
                    Tag = "geosite-category-ru",
                    FileName = "geosite-category-ru.srs",
                    Enabled = true,
                    Action = RuleSetAction.Direct
                }
            },
            DnsMode.Doh,
            isTunMode: false);

        Assert.True(withDirect.RequiresSniff);
        Assert.True(withDirect.RequiresSplitDns);
        Assert.Equal(new[] { "geosite-category-ru" }, withDirect.DirectRuleSetTags);
    }

    [Fact]
    public void RuleSetRoutingPolicy_SplitDnsOnlyForDohAndDirectTags()
    {
        var entries = new[]
        {
            new UserRuleSetEntry
            {
                Tag = "geosite-category-ru",
                FileName = "geosite-category-ru.srs",
                Enabled = true,
                Action = RuleSetAction.Direct
            },
            new UserRuleSetEntry
            {
                Tag = "geoip-ru",
                FileName = "geoip-ru.srs",
                Enabled = true,
                Action = RuleSetAction.Direct
            },
            new UserRuleSetEntry
            {
                Tag = "geosite-category-ru-ads",
                FileName = "geosite-category-ru@ads.srs",
                Enabled = true,
                Action = RuleSetAction.Block
            }
        };

        var doh = RuleSetRoutingPolicy.Evaluate(entries, DnsMode.Doh, isTunMode: true);
        Assert.True(doh.RequiresSplitDns);
        Assert.Equal(new[] { "geosite-category-ru", "geoip-ru" }, doh.DirectRuleSetTags);
        Assert.Equal(new[] { "geosite-category-ru" }, doh.DnsDirectRuleSetTags);

        var system = RuleSetRoutingPolicy.Evaluate(entries, DnsMode.System, isTunMode: true);
        Assert.False(system.RequiresSplitDns);
    }

    [Fact]
    public void SubscriptionUrlValidator_RequiresHttps()
    {
        SubscriptionUrlValidator.EnsureValid("https://example.com/sub/token");
        Assert.Throws<ArgumentException>(() => SubscriptionUrlValidator.EnsureValid("http://example.com/sub/token"));
        Assert.Throws<ArgumentException>(() => SubscriptionUrlValidator.EnsureValid("ftp://example.com/x"));
    }

    [Fact]
    public void InstallerDownloadUrlValidator_AllowlistsGithubHttpsAsset()
    {
        InstallerDownloadUrlValidator.EnsureValid(
            "https://github.com/EXTRBXY/vpn/releases/download/v1.0.0/NothingVpnSetup.exe",
            "NothingVpnSetup.exe");

        Assert.Throws<ArgumentException>(() =>
            InstallerDownloadUrlValidator.EnsureValid(
                "http://github.com/EXTRBXY/vpn/releases/download/v1.0.0/NothingVpnSetup.exe",
                "NothingVpnSetup.exe"));

        Assert.Throws<ArgumentException>(() =>
            InstallerDownloadUrlValidator.EnsureValid(
                "https://evil.example/NothingVpnSetup.exe",
                "NothingVpnSetup.exe"));

        Assert.Throws<ArgumentException>(() =>
            InstallerDownloadUrlValidator.EnsureValid(
                "https://github.com/EXTRBXY/vpn/releases/download/v1.0.0/malware.exe",
                "NothingVpnSetup.exe"));

        InstallerDownloadUrlValidator.EnsureValid(
            "https://objects.githubusercontent.com/github-production-release-asset/123/abc",
            "NothingVpnSetup.exe",
            requireAssetFileName: false);
    }
    [Theory]
    [InlineData("tun", true, "direct", "proxy")]
    [InlineData("tun", true, "proxy", "proxy")]
    [InlineData("tun", false, "direct", null)]
    [InlineData("tun_apps", true, "direct", null)]
    [InlineData("tun_apps", true, "proxy", null)]
    [InlineData("proxy", true, "direct", null)]
    public void ResolveSingBoxDohDetour_ReturnsExpected(
        string connectionMode,
        bool strictRoute,
        string userDetour,
        string? expected)
    {
        Assert.Equal(expected, TunBootstrapPolicy.ResolveSingBoxDohDetour(Mode(connectionMode), strictRoute, Detour(userDetour)));
    }

    [Theory]
    [InlineData(true, true, "bootstrap-local")]
    [InlineData(true, false, "bootstrap-local")]
    [InlineData(false, true, null)]
    public void ResolveDefaultDomainResolver_ReturnsBootstrapLocalForTun(bool useTun, bool useDoh, string? expected)
    {
        Assert.Equal(expected, TunBootstrapPolicy.ResolveDefaultDomainResolver(useTun, useDoh));
    }

    [Fact]
    public void CollectEndpointDomains_IncludesOnlyEndpointHost()
    {
        var domains = TunBootstrapPolicy.CollectEndpointDomains("node.example");
        Assert.Single(domains);
        Assert.Contains("node.example", domains, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::7")]
    [InlineData("")]
    [InlineData(null)]
    public void CollectEndpointDomains_IgnoresIpAndEmptyHosts(string? host)
    {
        Assert.Empty(TunBootstrapPolicy.CollectEndpointDomains(host));
    }

    [Theory]
    [InlineData("tun_apps", false)]
    [InlineData("tun", true)]
    [InlineData("proxy", true)]
    public void TunAppsPolicy_SplitTunnelFlags(string mode, bool userStrictRoute)
    {
        var isTunApps = string.Equals(mode, "tun_apps", StringComparison.Ordinal);
        Assert.Equal(isTunApps, TunAppsPolicy.IsTunApps(Mode(mode)));
        Assert.Equal(userStrictRoute && !isTunApps, TunAppsPolicy.UseStrictRoute(Mode(mode), userStrictRoute));
    }

    [Theory]
    [InlineData("tun", true)]
    [InlineData("tun_apps", true)]
    [InlineData("proxy", false)]
    public void TunRoutingPolicy_HijackDns_AllTunModes(string mode, bool expected)
    {
        Assert.Equal(expected, TunRoutingPolicy.HijackDns(Mode(mode)));
    }

    [Theory]
    [InlineData("tun", true)]
    [InlineData("tun_apps", false)]
    [InlineData("proxy", false)]
    public void TunRoutingPolicy_RouteQuicAndSecureDns_OnlyFullTun(string mode, bool expected)
    {
        Assert.Equal(expected, TunRoutingPolicy.RouteQuicThroughProxy(Mode(mode)));
        Assert.Equal(expected, TunRoutingPolicy.RouteSecureDnsThroughProxy(Mode(mode)));
        Assert.Equal(expected, TunRoutingPolicy.RouteIpv6ThroughProxy(Mode(mode)));
    }

    [Fact]
    public void TunRoutingPolicy_KnownSecureDnsDomains_IsNonEmpty()
    {
        Assert.NotEmpty(TunRoutingPolicy.KnownSecureDnsDomains);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(-1, 1500)]
    [InlineData(500, 576)]
    [InlineData(10000, 9000)]
    [InlineData(1500, 1500)]
    [InlineData(9000, 1500)]
    public void TunSettingsPolicy_NormalizeMtu_ClampsAndDefaults(int input, int expected)
    {
        Assert.Equal(expected, TunSettingsPolicy.NormalizeMtu(input));
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("198.18.1.1/30", true)]
    [InlineData("not-a-cidr", false)]
    [InlineData("10.0.0.1/33", false)]
    public void TunSettingsPolicy_IsValidAddressCidr(string cidr, bool expected)
    {
        Assert.Equal(expected, TunSettingsPolicy.IsValidAddressCidr(cidr));
    }

    [Fact]
    public void TunSettingsPolicy_Normalize_SanitizesInterfaceName()
    {
        var settings = new TunSettings { InterfaceName = "bad/name", Mtu = 1500 };
        TunSettingsPolicy.Normalize(settings);
        Assert.Equal("NothingVpn", settings.InterfaceName);
    }

    [Theory]
    [InlineData("gvisor", "gvisor")]
    [InlineData("system", "system")]
    [InlineData("unknown", "")]
    [InlineData("", "")]
    public void TunSettingsPolicy_ParseStack(string input, string expected)
    {
        Assert.Equal(expected, TunSettingsPolicy.Stacks.Parse(input).ToValue());
    }

    [Fact]
    public void ProxyConnectionPolicy_Normalize_EmptyUsesDefault()
    {
        var settings = new ProxyConnectionSettings { ProxyOverride = "   " };
        ProxyConnectionPolicy.Normalize(settings);
        Assert.Equal(ProxyConnectionPolicy.DefaultProxyOverride, settings.ProxyOverride);
    }

    [Fact]
    public void ProxyConnectionPolicy_Validate_RejectsControlChars()
    {
        var settings = new ProxyConnectionSettings { ProxyOverride = "localhost;\u0001" };
        Assert.Throws<ArgumentException>(() => ProxyConnectionPolicy.Validate(settings));
    }

    [Theory]
    [InlineData(DnsPreset.Cloudflare, "1.1.1.1", "cloudflare-dns.com")]
    [InlineData(DnsPreset.AdGuard, "94.140.14.14", "dns.adguard.com")]
    public void DnsPolicy_ApplyPreset_SetsPresetFields(DnsPreset preset, string server, string sni)
    {
        var settings = DnsPolicy.ApplyPreset(preset, new DnsSettings { DohServer = "x", DohSni = "y", DohPath = "/q" });

        Assert.Equal(server, settings.DohServer);
        Assert.Equal(sni, settings.DohSni);
        Assert.Equal("/dns-query", settings.DohPath);
        Assert.Equal(preset, DnsPolicy.DetectPreset(settings));
    }

    [Fact]
    public void DnsPolicy_ApplyCustomPreset_KeepsFields()
    {
        var settings = DnsPolicy.ApplyPreset(DnsPreset.Custom, new DnsSettings { DohServer = "10.0.0.53", DohSni = "dns.local", DohPath = "/q" });

        Assert.Equal("10.0.0.53", settings.DohServer);
        Assert.Equal("dns.local", settings.DohSni);
        Assert.Equal("/q", settings.DohPath);
    }

    private static ConnectionMode Mode(string value) => ConnectionPolicy.Modes.Parse(value);

    private static DnsDetour Detour(string value) => DnsPolicy.Detours.Parse(value);
}
