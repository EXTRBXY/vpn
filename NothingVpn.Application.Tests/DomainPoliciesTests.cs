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
}

