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
    public void NormalizeMode_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, ConnectionPolicy.NormalizeMode(input));
    }

    [Fact]
    public void EnsureTunAppsHasTargets_ThrowsWhenEmpty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectionPolicy.EnsureTunAppsHasTargets("tun_apps", Array.Empty<string>()));
        Assert.Contains("TUN", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DnsPolicy_Normalize_FillsDefaults()
    {
        var settings = new DnsSettings
        {
            Mode = "invalid",
            Detour = "invalid",
            DohPath = ""
        };

        DnsPolicy.Normalize(settings);

        Assert.Equal("doh", settings.Mode);
        Assert.Equal("direct", settings.Detour);
        Assert.Equal("/dns-query", settings.DohPath);
    }
    [Theory]
    [InlineData("tun", "proxy", "proxy")]
    [InlineData("proxy", "proxy", "proxy")]
    [InlineData("tun_apps", "proxy", "direct")]
    [InlineData("tun", "direct", "direct")]
    public void DnsDetourPolicy_EffectiveDetour_RespectsTunApps(string mode, string requested, string expected)
    {
        Assert.Equal(expected, DnsDetourPolicy.EffectiveDetour(mode, requested));
    }

    [Fact]
    public void DnsPolicy_Validate_RequiresSniForDoh()
    {
        var ok = new DnsSettings
        {
            Mode = "doh",
            DohServer = "8.8.8.8",
            DohSni = "dns.google",
            DohPath = "/dns-query"
        };
        DnsPolicy.Validate(ok);

        var bad = new DnsSettings
        {
            Mode = "doh",
            DohServer = "8.8.8.8",
            DohSni = "",
            DohPath = "/dns-query"
        };
        Assert.Throws<InvalidOperationException>(() => DnsPolicy.Validate(bad));
    }

    [Theory]
    [InlineData("1.1.1.1", "cloudflare-dns.com", "/dns-query", 0)]
    [InlineData("8.8.8.8", "dns.google", "/dns-query", 1)]
    [InlineData("9.9.9.9", "dns.quad9.net", "/dns-query", 2)]
    [InlineData("94.140.14.14", "dns.adguard.com", "/dns-query", 3)]
    [InlineData("8.8.8.8", "dns.google", "/custom", 4)]
    public void DnsPolicy_StateToPresetIndex_ReflectsActualDnsFields(
        string server, string sni, string path, int expected)
    {
        var settings = new DnsSettings { DohServer = server, DohSni = sni, DohPath = path };

        Assert.Equal(expected, DnsPolicy.StateToPresetIndex(settings));
    }

    [Fact]
    public void RuleSetRoutingPolicy_TunAlwaysRequiresSniff()
    {
        var decision = RuleSetRoutingPolicy.Evaluate(Array.Empty<UserRuleSetEntry>(), "system", isTunMode: true);
        Assert.True(decision.RequiresSniff);
        Assert.False(decision.RequiresSplitDns);
        Assert.Empty(decision.DirectRuleSetTags);
    }

    [Fact]
    public void RuleSetRoutingPolicy_ProxyRequiresSniffOnlyWithEnabledRuleSets()
    {
        var empty = RuleSetRoutingPolicy.Evaluate(Array.Empty<UserRuleSetEntry>(), "doh", isTunMode: false);
        Assert.False(empty.RequiresSniff);

        var withDirect = RuleSetRoutingPolicy.Evaluate(
            new[]
            {
                new UserRuleSetEntry
                {
                    Tag = "geosite-category-ru",
                    FileName = "geosite-category-ru.srs",
                    Enabled = true,
                    Action = "direct"
                }
            },
            "doh",
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
                Action = "direct"
            },
            new UserRuleSetEntry
            {
                Tag = "geoip-ru",
                FileName = "geoip-ru.srs",
                Enabled = true,
                Action = "direct"
            },
            new UserRuleSetEntry
            {
                Tag = "geosite-category-ru-ads",
                FileName = "geosite-category-ru@ads.srs",
                Enabled = true,
                Action = "block"
            }
        };

        var doh = RuleSetRoutingPolicy.Evaluate(entries, "doh", isTunMode: true);
        Assert.True(doh.RequiresSplitDns);
        Assert.Equal(new[] { "geosite-category-ru", "geoip-ru" }, doh.DirectRuleSetTags);
        Assert.Equal(new[] { "geosite-category-ru" }, doh.DnsDirectRuleSetTags);

        var system = RuleSetRoutingPolicy.Evaluate(entries, "system", isTunMode: true);
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
        Assert.Equal(expected, TunBootstrapPolicy.ResolveSingBoxDohDetour(connectionMode, strictRoute, userDetour));
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
        Assert.Equal(isTunApps, TunAppsPolicy.IsTunApps(mode));
        Assert.Equal(userStrictRoute && !isTunApps, TunAppsPolicy.UseStrictRoute(mode, userStrictRoute));
    }

    [Theory]
    [InlineData("tun", true)]
    [InlineData("tun_apps", true)]
    [InlineData("proxy", false)]
    public void TunRoutingPolicy_HijackDns_AllTunModes(string mode, bool expected)
    {
        Assert.Equal(expected, TunRoutingPolicy.HijackDns(mode));
    }

    [Theory]
    [InlineData("tun", true)]
    [InlineData("tun_apps", false)]
    [InlineData("proxy", false)]
    public void TunRoutingPolicy_RouteQuicAndSecureDns_OnlyFullTun(string mode, bool expected)
    {
        Assert.Equal(expected, TunRoutingPolicy.RouteQuicThroughProxy(mode));
        Assert.Equal(expected, TunRoutingPolicy.RouteSecureDnsThroughProxy(mode));
        Assert.Equal(expected, TunRoutingPolicy.RouteIpv6ThroughProxy(mode));
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
    public void TunSettingsPolicy_NormalizeStack(string input, string expected)
    {
        Assert.Equal(expected, TunSettingsPolicy.NormalizeStack(input));
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

    [Fact]
    public void DnsPolicy_IsDohMode_DetectsMode()
    {
        Assert.True(DnsPolicy.IsDohMode(new DnsSettings { Mode = "doh" }));
        Assert.False(DnsPolicy.IsDohMode(new DnsSettings { Mode = "system" }));
    }

    [Theory]
    [InlineData("proxy", 1, "Через VPN")]
    [InlineData("direct", 0, "Напрямую")]
    public void DnsPolicy_DetourUiMapping(string detour, int expectedIndex, string expectedLabel)
    {
        Assert.Equal(expectedIndex, DnsPolicy.DetourToComboIndex(detour));
        Assert.Equal(detour, DnsPolicy.ComboIndexToDetour(expectedIndex));
        Assert.Equal(expectedLabel, DnsPolicy.DetourToDisplayLabel(detour));
    }
}
