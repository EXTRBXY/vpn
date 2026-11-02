using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Profile;

namespace NothingVpn.Application.Tests;

/// <summary>Ссылки VLESS с транспортом XHTTP: разбор, extra, обратная совместимость Id и форматтер.</summary>
public sealed class XhttpLinkParserTests
{
    private const string Prefix = "vless://11111111-2222-3333-4444-555555555555@node.example.com:443";

    [Fact]
    public void Parse_MapsXhttpFieldsSeparatelyFromWebSocket()
    {
        var profile = VlessLinkParser.Parse(
            $"{Prefix}?type=xhttp&security=tls&sni=cdn.example.org&path=%2Fxh&host=cdn.example.org&mode=stream-up" +
            "&flow=xtls-rprx-vision#xh");

        Assert.Equal("xhttp", profile.Type);
        Assert.Equal("/xh", profile.XhttpPath);
        Assert.Equal("cdn.example.org", profile.XhttpHost);
        Assert.Equal(XhttpMode.StreamUp, profile.XhttpMode);
        Assert.Null(profile.WsPath);
        Assert.Null(profile.WsHost);
        Assert.Null(profile.Flow);
        Assert.Empty(profile.ExtraQuery);
        Assert.Equal(OutboundEngine.Auto, profile.Engine);
    }

    [Fact]
    public void Parse_TreatsSplitHttpAsXhttp()
    {
        var profile = VlessLinkParser.Parse($"{Prefix}?type=splithttp&security=tls&path=%2Fsplit#legacy");

        Assert.Equal("xhttp", profile.Type);
        Assert.Equal("/split", profile.XhttpPath);
        Assert.Equal(XhttpMode.Auto, profile.XhttpMode);
    }

    [Fact]
    public void Parse_RejectsUnknownMode()
    {
        Assert.Throws<ArgumentException>(() => VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&mode=turbo"));
    }

    [Fact]
    public void Parse_ReadsTypedExtraAndDropsServerOnlyKeys()
    {
        const string extra = """
            {"headers":{"User-Agent":"ua"},"xPaddingBytes":"100-1000","noGRPCHeader":true,"scMaxEachPostBytes":1000000,
             "noSSEHeader":true,"scMaxBufferedPosts":30,"xmux":{"maxConcurrency":"8-16","hKeepAlivePeriod":-1}}
            """;

        var profile = VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&extra={Uri.EscapeDataString(extra)}");

        var parsed = profile.XhttpExtra!;
        Assert.Equal("ua", parsed.Headers!["User-Agent"]);
        Assert.Equal("100-1000", parsed.XPaddingBytes);
        Assert.True(parsed.NoGrpcHeader);
        Assert.Equal("1000000", parsed.ScMaxEachPostBytes);
        Assert.Equal("8-16", parsed.Xmux!.MaxConcurrency);
        Assert.Equal(-1, parsed.Xmux.HKeepAlivePeriod);
    }

    [Theory]
    [InlineData("""{"downloadSettings":{}}""")]
    [InlineData("""{"h2Params":{}}""")]
    [InlineData("""{"extra":{}}""")]
    [InlineData("""{"unknownField":1}""")]
    [InlineData("""{"xmux":{"unknown":1}}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{not json""")]
    [InlineData("""{"headers":{"Host":"evil"}}""")]
    [InlineData("""{"xPaddingBytes":"9-1"}""")]
    public void Parse_RejectsUnsupportedOrInvalidExtra(string extra)
    {
        Assert.Throws<ArgumentException>(() =>
            VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&extra={Uri.EscapeDataString(extra)}"));
    }

    [Fact]
    public void Parse_KeepsModeAndExtraOfOtherTransportsInExtraQuery()
    {
        var profile = VlessLinkParser.Parse($"{Prefix}?type=grpc&security=tls&serviceName=svc&mode=multi&extra=x");

        Assert.Equal("multi", profile.ExtraQuery["mode"]);
        Assert.Equal("x", profile.ExtraQuery["extra"]);
        Assert.Null(profile.XhttpExtra);
    }

    [Fact]
    public void Parse_KeepsIdOfExistingTransports()
    {
        // Id профиля — инвариант хранения: поля XHTTP не должны менять его для tcp/ws/grpc.
        var profile = VlessLinkParser.Parse($"{Prefix}?type=ws&security=tls&sni=cdn.example.org&path=%2Fws&host=cdn.example.org");

        Assert.Equal("b0616b0c33be0f1c", profile.Id);
    }

    [Fact]
    public void Parse_IdDependsOnXhttpFields()
    {
        var a = VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&path=%2Fa");
        var b = VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&path=%2Fb");
        var c = VlessLinkParser.Parse($"{Prefix}?type=xhttp&security=tls&path=%2Fa&mode=packet-up");

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public void Formatter_RoundtripsXhttpProfile()
    {
        const string extra = """{"xPaddingBytes":"100-1000","xmux":{"maxConnections":"4"}}""";
        var original = VlessLinkParser.Parse(
            $"{Prefix}?type=xhttp&security=reality&sni=www.example.org&pbk=key&sid=a1&path=%2Fxh&host=h.example" +
            $"&mode=stream-one&extra={Uri.EscapeDataString(extra)}#node");

        var restored = VlessLinkParser.Parse(VlessLinkFormatter.Build(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.XhttpPath, restored.XhttpPath);
        Assert.Equal(original.XhttpHost, restored.XhttpHost);
        Assert.Equal(XhttpMode.StreamOne, restored.XhttpMode);
        Assert.Equal("100-1000", restored.XhttpExtra!.XPaddingBytes);
        Assert.Equal("4", restored.XhttpExtra.Xmux!.MaxConnections);
    }
}
