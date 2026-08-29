using NothingVpn.Domain.Subscriptions;

namespace NothingVpn.Application.Tests;

public sealed class SubscriptionBodyDecoderTests
{
    [Fact]
    public void DecodeBody_PlainVless_ReturnsAsIs()
    {
        const string body = "vless://uuid@host:443?security=reality#node1\nvless://uuid2@host2:443#node2";
        var decoded = SubscriptionBodyDecoder.DecodeBody(body);
        Assert.Equal(body, decoded);
    }

    [Fact]
    public void DecodeBody_Base64_ReturnsDecoded()
    {
        const string plain = "vless://uuid@host:443#node1";
        var body = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
        var decoded = SubscriptionBodyDecoder.DecodeBody(body);
        Assert.Equal(plain, decoded);
    }

    [Fact]
    public void DecodeBody_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionBodyDecoder.DecodeBody("  "));
    }

    [Fact]
    public void Extract_SkipsNonVlessAndCounts()
    {
        const string body = """
            vless://uuid@host:443#n1
            vmess://ignored
            trojan://ignored2
            vless://uuid2@host2:443#n2
            """;

        var result = SubscriptionLinkExtractor.Extract(body);
        Assert.Equal(2, result.VlessLinks.Count);
        Assert.Equal(2, result.SkippedNonVlessLines);
    }
}
