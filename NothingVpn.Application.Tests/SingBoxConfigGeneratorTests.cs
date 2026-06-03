using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.SingBox;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Application.Tests;

public sealed class SingBoxConfigGeneratorTests
{
    [Theory]
    [InlineData("tun")]
    [InlineData("tun_apps")]
    public void Build_TunModes_DefaultDomainResolverMatchesDnsServerTag(string mode)
    {
        var paths = AppPaths.CreateDefault();
        var profile = new VlessProfile
        {
            Id = "testprof",
            Host = "node.example",
            Port = 443,
            Uuid = Guid.NewGuid().ToString(),
            Security = "tls",
            Sni = "node.example"
        };
        var state = new AppState
        {
            Mode = mode,
            DnsMode = mode == "tun" ? "doh" : "doh",
            TunAppProcessPaths = mode == "tun_apps"
                ? ["C:\\Apps\\firefox.exe"]
                : []
        };

        var config = SingBoxConfigGenerator.Build(paths, profile, state);

        var resolver = config.Route.DefaultDomainResolver;
        Assert.False(string.IsNullOrWhiteSpace(resolver));

        var tags = config.Dns!.Servers.Select(x => x.Tag).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(resolver, tags);
    }
}
