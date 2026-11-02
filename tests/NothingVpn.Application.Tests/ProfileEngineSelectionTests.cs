using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

/// <summary>Выбранный пользователем движок профиля переживает импорт, правку и синхронизацию подписки.</summary>
public sealed class ProfileEngineSelectionTests : IDisposable
{
    private const string RealityLink =
        "vless://11111111-2222-3333-4444-555555555555@node.example.com:443?type=tcp&security=reality" +
        "&sni=www.example.org&pbk=key&sid=a1&flow=xtls-rprx-vision#node";

    private const string XhttpLink =
        "vless://11111111-2222-3333-4444-555555555555@node.example.com:443?type=xhttp&security=tls&path=%2Fxh#xh";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));
    private readonly ProfileStorePort _store;
    private readonly ProfileService _service;

    public ProfileEngineSelectionTests()
    {
        Directory.CreateDirectory(_root);
        _store = new ProfileStorePort(new FakeAppPathsPort(_root));
        _service = new ProfileService(_store, new ProfileParserPort());
    }

    [Fact]
    public void Upsert_StoresChosenEngine()
    {
        var saved = _service.UpsertFromVlessLink(RealityLink, null, OutboundEngine.SingBox);

        Assert.Equal(OutboundEngine.SingBox, saved.Engine);
        Assert.Equal(OutboundEngine.SingBox, _store.Load().Single().Engine);
    }

    [Fact]
    public void Upsert_RejectsSingBoxForXhttp()
    {
        Assert.Throws<ArgumentException>(() => _service.UpsertFromVlessLink(XhttpLink, null, OutboundEngine.SingBox));
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Import_KeepsEngineOfExistingProfile()
    {
        _service.UpsertFromVlessLink(RealityLink, null, OutboundEngine.SingBox);

        _service.ImportFromVlessLink(RealityLink);

        Assert.Equal(OutboundEngine.SingBox, _store.Load().Single().Engine);
    }

    [Fact]
    public void SubscriptionSync_KeepsEngineOfExistingProfile()
    {
        var fromSubscription = new ProfileParserPort().ParseVlessLink(RealityLink);
        fromSubscription.SubscriptionId = "sub";
        _store.SyncForSubscription("sub", [fromSubscription]);
        var stored = _store.Load().Single();
        stored.Engine = OutboundEngine.SingBox;
        _store.Upsert(stored);

        var refreshed = new ProfileParserPort().ParseVlessLink(RealityLink);
        refreshed.SubscriptionId = "sub";
        _store.SyncForSubscription("sub", [refreshed]);

        Assert.Equal(OutboundEngine.SingBox, _store.Load().Single().Engine);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeAppPathsPort(string root) : IAppPathsPort
    {
        public AppPathsModel Get() => new()
        {
            BaseDir = root,
            ConfigsDir = Path.Combine(root, "configs"),
            RuleSetsDir = Path.Combine(root, "rulesets"),
            LogsDir = Path.Combine(root, "logs"),
            ProfilesJsonPath = Path.Combine(root, "profiles.json"),
            SubscriptionsJsonPath = Path.Combine(root, "subscriptions.json"),
            StateJsonPath = Path.Combine(root, "state.json")
        };
    }
}
