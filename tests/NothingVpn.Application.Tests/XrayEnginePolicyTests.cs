using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Tests;

/// <summary>Выбор движка профиля и политика XHTTP/XMUX (ADR-0002).</summary>
public sealed class XrayEnginePolicyTests
{
    [Theory]
    [InlineData(OutboundEngine.Auto, "tcp", "tls", OutboundEngine.SingBox)]
    [InlineData(OutboundEngine.Auto, "ws", "none", OutboundEngine.SingBox)]
    [InlineData(OutboundEngine.Auto, "tcp", "reality", OutboundEngine.Xray)]
    [InlineData(OutboundEngine.Auto, "xhttp", "tls", OutboundEngine.Xray)]
    [InlineData(OutboundEngine.SingBox, "tcp", "reality", OutboundEngine.SingBox)]
    [InlineData(OutboundEngine.Xray, "grpc", "tls", OutboundEngine.Xray)]
    [InlineData((OutboundEngine)99, "tcp", "reality", OutboundEngine.Xray)]
    public void Resolve_PicksEngineByPreferenceAndTransport(
        OutboundEngine preference, string transport, string security, OutboundEngine expected)
    {
        Assert.Equal(expected, OutboundEnginePolicy.Resolve(preference, transport, security));
    }

    [Fact]
    public void Resolve_RejectsSingBoxForXhttp()
    {
        var error = Assert.Throws<ArgumentException>(
            () => OutboundEnginePolicy.Resolve(OutboundEngine.SingBox, "xhttp", "tls"));

        Assert.Equal("Транспорт XHTTP поддерживает только движок Xray.", error.Message);
    }

    [Theory]
    [InlineData(OutboundEngine.Auto, "auto")]
    [InlineData(OutboundEngine.SingBox, "sing-box")]
    [InlineData(OutboundEngine.Xray, "xray")]
    public void Preferences_HavePinnedStorageValues(OutboundEngine engine, string value)
    {
        Assert.Equal(value, OutboundEnginePolicy.Preferences.ToValue(engine));
        Assert.True(OutboundEnginePolicy.Preferences.TryParse(value, out var parsed));
        Assert.Equal(engine, parsed);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("16-32", true)]
    [InlineData("7", true)]
    [InlineData("32-16", false)]
    [InlineData("-1", false)]
    [InlineData("1-", false)]
    [InlineData("abc", false)]
    [InlineData("1234567890", false)]
    [InlineData("", false)]
    public void IsValidRange_AcceptsXrayInt32Range(string value, bool expected)
    {
        Assert.Equal(expected, XhttpPolicy.IsValidRange(value));
    }

    [Fact]
    public void NormalizeTransport_MapsLegacySplitHttp()
    {
        Assert.Equal("xhttp", XhttpPolicy.NormalizeTransport("splithttp"));
        Assert.Equal("ws", XhttpPolicy.NormalizeTransport("ws"));
    }

    [Fact]
    public void Normalize_ReplacesInvalidXmuxWithDefaults()
    {
        var settings = new XmuxSettings
        {
            MaxConcurrency = "bad",
            MaxConnections = "4",
            CMaxReuseTimes = "9-1",
            HMaxRequestTimes = " 100-200 ",
            HMaxReusableSecs = ""
        };

        XhttpPolicy.Normalize(settings);

        Assert.Equal(XhttpPolicy.DefaultXmuxMaxConcurrency, settings.MaxConcurrency);
        Assert.Equal(XhttpPolicy.DefaultXmuxMaxConnections, settings.MaxConnections);
        Assert.Equal(XhttpPolicy.DefaultXmuxCMaxReuseTimes, settings.CMaxReuseTimes);
        Assert.Equal("100-200", settings.HMaxRequestTimes);
        Assert.Equal(XhttpPolicy.DefaultXmuxHMaxReusableSecs, settings.HMaxReusableSecs);
    }

    [Fact]
    public void Validate_RejectsConcurrencyTogetherWithConnections()
    {
        var settings = new XmuxSettings { MaxConcurrency = "16", MaxConnections = "4" };

        Assert.Throws<ArgumentException>(() => XhttpPolicy.Validate(settings));
    }

    [Fact]
    public void Validate_AcceptsDefaults()
    {
        XhttpPolicy.Validate(new XmuxSettings());
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("bad header")]
    public void ProfileValidator_RejectsForbiddenHeaders(string header)
    {
        var profile = XhttpProfile();
        profile.XhttpExtra = new XhttpExtraModel { Headers = new Dictionary<string, string> { [header] = "v" } };

        Assert.Throws<ArgumentException>(() => XhttpProfileValidator.Validate(profile));
    }

    [Fact]
    public void ProfileValidator_RejectsInvalidRangeAndControlCharacters()
    {
        var range = XhttpProfile();
        range.XhttpExtra = new XhttpExtraModel { XPaddingBytes = "10-1" };
        var text = XhttpProfile();
        text.XhttpPath = "/a\nb";
        var xmux = XhttpProfile();
        xmux.XhttpExtra = new XhttpExtraModel { Xmux = new XmuxModel { MaxConcurrency = "8", MaxConnections = "2" } };

        Assert.Throws<ArgumentException>(() => XhttpProfileValidator.Validate(range));
        Assert.Throws<ArgumentException>(() => XhttpProfileValidator.Validate(text));
        Assert.Throws<ArgumentException>(() => XhttpProfileValidator.Validate(xmux));
    }

    [Fact]
    public void ProfileValidator_IgnoresOtherTransports()
    {
        var profile = XhttpProfile();
        profile.Type = "ws";
        profile.XhttpPath = "/a\nb";

        XhttpProfileValidator.Validate(profile);
    }

    private static VpnProfile XhttpProfile() => new()
    {
        Id = "p1",
        Uuid = "11111111-2222-3333-4444-555555555555",
        Host = "node.example.com",
        Port = 443,
        Type = "xhttp",
        Security = "tls",
        XhttpPath = "/xh"
    };
}
