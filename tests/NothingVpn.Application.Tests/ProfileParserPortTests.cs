using NothingVpn.Infrastructure.Profile;

namespace NothingVpn.Application.Tests;

public sealed class ProfileParserPortTests
{
    [Fact]
    public void Parse_DifferentRealityShortId_YieldsDifferentProfileIds()
    {
        const string baseQuery =
            "encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&sni=example.com" +
            "&fp=chrome&pbk=YWJjZGVmZ2hpams=";

        var a = VlessLinkParser.Parse(
            $"vless://11111111-1111-1111-1111-111111111111@node.example:443?{baseQuery}&sid=aaaa#node-a");
        var b = VlessLinkParser.Parse(
            $"vless://11111111-1111-1111-1111-111111111111@node.example:443?{baseQuery}&sid=bbbbbbbbbbbb#node-b");

        Assert.NotEqual(a.Id, b.Id);
    }
}
