using NothingVpn.Domain.Subscriptions;

namespace NothingVpn.Application.Tests;

public sealed class SubscriptionUserinfoParserTests
{
    [Fact]
    public void TryParse_3xUiHeader_ParsesAllFields()
    {
        const string header = "upload=100; download=200; total=1000; expire=1700000000";
        var ok = SubscriptionUserinfoParser.TryParse(header, out var info);
        Assert.True(ok);
        Assert.Equal(100, info.Upload);
        Assert.Equal(200, info.Download);
        Assert.Equal(1000, info.Total);
        Assert.NotNull(info.ExpireUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), info.ExpireUtc);
    }

    [Fact]
    public void TryParse_MissingHeader_ReturnsFalse()
    {
        var ok = SubscriptionUserinfoParser.TryParse(null, out _);
        Assert.False(ok);
    }

    [Fact]
    public void HeadersParser_ReadsIntervalAndBase64Title()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Subscription-Userinfo"] = "upload=0; download=0; total=0; expire=0",
            ["Profile-Update-Interval"] = "12",
            ["Profile-Title"] = "base64:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("My Panel"))
        };

        var parsed = SubscriptionHeadersParser.Parse(headers);
        Assert.Equal(12, parsed.UpdateIntervalHours);
        Assert.Equal("My Panel", parsed.ProfileTitle);
        Assert.NotNull(parsed.UserInfo);
    }
}
