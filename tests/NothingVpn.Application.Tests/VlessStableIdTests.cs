using NothingVpn.Infrastructure.Profile;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Id профиля хранится в state.json (activeProfileId) и сопоставляет профили подписок между обновлениями.
/// Значения ниже получены текущим кодом; их изменение означает миграцию данных пользователей.
/// </summary>
public sealed class VlessStableIdTests
{
    [Theory]
    [InlineData(
        "vless://11111111-2222-3333-4444-555555555555@tls.example.com:443?type=tcp&security=tls&sni=tls.example.com&fp=chrome&alpn=h2,http/1.1#TLS",
        "0ede241cbb541b11")]
    [InlineData(
        "vless://66666666-7777-8888-9999-AAAAAAAAAAAA@203.0.113.5:8443?type=tcp&security=reality&sni=www.example.org&fp=chrome&pbk=pUbLiCkEy0123456789abcdefghijklmnopqrstuv&sid=a1b2c3d4&flow=xtls-rprx-vision#Reality",
        "0169e2ba71cde403")]
    [InlineData(
        "vless://bbbbbbbb-cccc-dddd-eeee-ffffffffffff@ws.example.net:2083?type=ws&security=tls&sni=cdn.example.net&path=%2Fws%3Fed%3D2048&host=cdn.example.net#WS",
        "eca26a80bd2370a1")]
    [InlineData(
        "vless://01234567-89ab-cdef-0123-456789abcdef@grpc.example.org:443?type=grpc&security=tls&sni=grpc.example.org&serviceName=vless-grpc#gRPC",
        "7a70326ffc6a82e6")]
    public void Parse_ProducesStableId(string link, string expectedId)
    {
        var profile = VlessLinkParser.Parse(link);

        Assert.Equal(expectedId, profile.Id);
    }

    [Fact]
    public void Parse_IdIgnoresNameAndUnknownQueryParameters()
    {
        var a = VlessLinkParser.Parse(
            "vless://11111111-2222-3333-4444-555555555555@tls.example.com:443?type=tcp&security=tls&sni=tls.example.com#First");
        var b = VlessLinkParser.Parse(
            "vless://11111111-2222-3333-4444-555555555555@tls.example.com:443?type=tcp&security=tls&sni=tls.example.com&ech=x&fp=firefox&alpn=h2#Second");

        Assert.Equal(a.Id, b.Id);
    }
}
