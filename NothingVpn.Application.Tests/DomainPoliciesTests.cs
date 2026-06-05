using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

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
    [InlineData("tun", true, "direct", "proxy")]
    [InlineData("tun", true, "proxy", "proxy")]
    [InlineData("tun", false, "direct", null)]
    [InlineData("tun_apps", true, "direct", null)]
    [InlineData("tun_apps", true, "proxy", "proxy")]
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
    public void CollectEndpointDomains_IncludesHostAndDistinctSni()
    {
        var domains = TunBootstrapPolicy.CollectEndpointDomains("node.example", "cdn.example");
        Assert.Equal(2, domains.Count);
        Assert.Contains("node.example", domains, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("cdn.example", domains, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectEndpointDomains_DeduplicatesMatchingHostAndSni()
    {
        Assert.Single(TunBootstrapPolicy.CollectEndpointDomains("node.example", "node.example"));
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
    public void TunRoutingPolicy_HijackDns_ForAllTunModes(string mode, bool expected)
    {
        Assert.Equal(expected, TunRoutingPolicy.HijackDns(mode));
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
