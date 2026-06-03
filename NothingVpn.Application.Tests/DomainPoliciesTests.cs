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
    [InlineData("tun_apps", false, true)]
    [InlineData("tun", true, false)]
    [InlineData("proxy", true, false)]
    public void TunAppsPolicy_SplitTunnelFlags(string mode, bool userStrictRoute, bool expectedSystemDns)
    {
        Assert.Equal(expectedSystemDns, TunAppsPolicy.UseSystemDnsOnly(mode));
        Assert.Equal(!expectedSystemDns, TunAppsPolicy.HijackDns(mode));
        Assert.Equal(userStrictRoute && !expectedSystemDns, TunAppsPolicy.UseStrictRoute(mode, userStrictRoute));
    }
}
