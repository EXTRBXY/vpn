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
        var actual = ConnectionPolicy.NormalizeMode(input);
        Assert.Equal(expected, actual);
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
}

