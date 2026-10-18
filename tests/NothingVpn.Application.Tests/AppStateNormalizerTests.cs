using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Нормализация состояния — одна точка (AppStateNormalizer); генератор sing-box получает уже
/// нормализованное состояние и сам значения не исправляет.
/// </summary>
public sealed class AppStateNormalizerTests
{
    private static readonly AppPaths Paths = new(
        @"C:\NothingVpnTest", @"C:\NothingVpnTest\configs", @"C:\NothingVpnTest\rulesets", @"C:\NothingVpnTest\logs",
        @"C:\NothingVpnTest\profiles.json", @"C:\NothingVpnTest\subscriptions.json", @"C:\NothingVpnTest\state.json");

    [Fact]
    public void Normalize_AppliesDomainPoliciesToEveryConnectionField()
    {
        var state = new AppStateModel
        {
            Mode = " TUN ",
            DnsMode = "garbage",
            DohServer = " 9.9.9.9 ",
            DohPath = " ",
            DohSni = " dns.quad9.net ",
            DnsDetour = "PROXY",
            TunInterfaceName = "bad:name",
            TunAddressCidr = "172.19.0.1/30",
            TunMtu = 9000,
            TunStack = " GVISOR ",
            ProxyOverride = "",
            SingBoxLogLevel = "Warning",
            CloseBehavior = "EXIT",
            TunAppProcessPaths = [@" C:\Apps\one.exe ", @"C:\APPS\ONE.EXE", "relative.exe"]
        };

        AppStateNormalizer.Normalize(state, new PathPolicyPort());

        Assert.Equal("tun", state.Mode);
        Assert.Equal("doh", state.DnsMode);
        Assert.Equal("9.9.9.9", state.DohServer);
        Assert.Equal("/dns-query", state.DohPath);
        Assert.Equal("dns.quad9.net", state.DohSni);
        Assert.Equal("proxy", state.DnsDetour);
        Assert.Equal("NothingVpn", state.TunInterfaceName);
        Assert.Equal("auto", state.TunAddressCidr);
        Assert.Equal(1500, state.TunMtu);
        Assert.Equal("gvisor", state.TunStack);
        Assert.Equal(ProxyConnectionPolicy.DefaultProxyOverride, state.ProxyOverride);
        Assert.Equal("warn", state.SingBoxLogLevel);
        Assert.Equal(AppCloseBehavior.Exit, state.CloseBehavior);
        Assert.Equal([@"C:\Apps\one.exe"], state.TunAppProcessPaths);
    }

    [Fact]
    public void Normalize_DefaultStateIsAlreadyNormalized()
    {
        var state = new AppStateModel();
        var before = JsonSerializer.Serialize(state);

        AppStateNormalizer.Normalize(state, new PathPolicyPort());

        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("tun")]
    [InlineData("tun_apps")]
    [InlineData("proxy")]
    public void Generator_OnNormalizedRawState_MatchesCleanState(string mode)
    {
        var raw = new AppStateModel
        {
            Mode = mode.ToUpperInvariant(),
            DnsMode = " DOH ",
            DohServer = " 8.8.8.8 ",
            DohPath = "",
            DohSni = " dns.google ",
            TunInterfaceName = " NothingVpn ",
            TunAddressCidr = " AUTO ",
            TunMtu = 0,
            TunStack = "unknown",
            SingBoxLogLevel = "WARN",
            TunAppProcessPaths = [@"C:\Apps\browser.exe", @"c:\apps\BROWSER.exe "]
        };
        var clean = new AppStateModel { Mode = mode, TunAppProcessPaths = [@"C:\Apps\browser.exe"] };

        AppStateNormalizer.Normalize(raw, new PathPolicyPort());

        Assert.Equal(BuildJson(clean), BuildJson(raw));
    }

    [Fact]
    public void Generator_EmptyDohServer_FallsBackToDefaultServer()
    {
        // До шага 3 генератор подставлял 1.1.1.1, хотя значение по умолчанию везде 8.8.8.8 (REFACTORING 2.5).
        var state = new AppStateModel { Mode = "proxy", DohServer = "" };
        AppStateNormalizer.Normalize(state, new PathPolicyPort());

        var config = SingBoxConfigGenerator.Build(Paths, Profile(), state);

        Assert.Equal(DnsPolicy.DefaultDohServer, Assert.Single(config.Dns!.Servers, s => s.Tag == "doh").Server);
    }

    [Theory]
    [InlineData(null, "info")]
    [InlineData("", "info")]
    [InlineData("verbose", "info")]
    [InlineData(" Warning ", "warn")]
    [InlineData("WARN", "warn")]
    [InlineData("debug", "debug")]
    [InlineData("panic", "panic")]
    public void SingBoxLogLevelPolicy_Normalize(string? input, string expected) =>
        Assert.Equal(expected, SingBoxLogLevelPolicy.Normalize(input));

    [Fact]
    public void TunInterfacePolicy_KeepsProfileDerivedValuesStable()
    {
        Assert.Equal("NothingVpn-012345", TunInterfacePolicy.InterfaceName("NothingVpn", "0123456789abcdef"));
        Assert.Equal("NothingVpn-ab", TunInterfacePolicy.InterfaceName("NothingVpn", "ab"));
        Assert.Equal("198.18.26.161/30", TunInterfacePolicy.Ipv4Address("auto", "0123456789abcdef"));
        Assert.Equal("198.18.26.161/30", TunInterfacePolicy.Ipv4Address("172.19.0.1/30", "0123456789abcdef"));
        Assert.Equal("10.1.2.1/30", TunInterfacePolicy.Ipv4Address("10.1.2.1/30", "0123456789abcdef"));
        Assert.Equal("fdfe:dcba:9876:97ab::1/126", TunInterfacePolicy.Ipv6Address("0123456789abcdef"));
        Assert.Equal("198.18.201.161/30", TunInterfacePolicy.Ipv4Address("auto", "fedcba9876543210"));
        Assert.Equal("fdfe:dcba:9876:6018::1/126", TunInterfacePolicy.Ipv6Address("fedcba9876543210"));
        Assert.Equal("system", TunInterfacePolicy.SingBoxStack(""));
        Assert.Equal("gvisor", TunInterfacePolicy.SingBoxStack("gvisor"));
    }

    private static string BuildJson(AppStateModel state) =>
        JsonSerializer.Serialize(SingBoxConfigGenerator.Build(Paths, Profile(), state));

    private static VpnProfile Profile() => new()
    {
        Id = "0123456789abcdef",
        Uuid = "11111111-2222-3333-4444-555555555555",
        Host = "node.example.com",
        Port = 443,
        Security = "tls",
        Sni = "node.example.com"
    };
}
