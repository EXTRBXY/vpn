using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Файлы текущей схемы (NV1 + DPAPI CurrentUser + camelCase JSON) должны читаться после рефакторинга.
/// JSON ниже — фиксированный снимок схемы версии 0.6.8; не обновляйте его вслед за кодом.
/// </summary>
public sealed class StorageFormatCompatibilityTests : IDisposable
{
    private const string StateJson = """
        {
          "activeProfileId": "a1b2c3d4e5f60718",
          "mode": "tun",
          "tunAppProcessPaths": [
            "C:\\Program Files\\App\\app.exe"
          ],
          "userRuleSets": [
            {
              "tag": "geosite-category-ru",
              "name": "Geosite: категория RU",
              "fileName": "geosite-category-ru.srs",
              "enabled": true,
              "action": "direct",
              "builtinId": "sing-geosite:category-ru",
              "remoteEtag": "\"abc123\"",
              "lastDownloadedUtc": "2026-09-01T10:20:30+03:00"
            },
            {
              "tag": "geosite-category-ru-ads",
              "name": "Geosite: категория RU — реклама",
              "fileName": "geosite-category-ru@ads.srs",
              "enabled": false,
              "action": "block",
              "builtinId": "sing-geosite:category-ru-ads",
              "remoteEtag": null,
              "lastDownloadedUtc": null
            },
            {
              "tag": "geoip-ru",
              "name": "GeoIP: RU",
              "fileName": "geoip-ru.srs",
              "enabled": false,
              "action": "direct",
              "builtinId": "sing-geoip:ru",
              "remoteEtag": null,
              "lastDownloadedUtc": null
            },
            {
              "tag": "user-ruleset-0123456789ab",
              "name": "Custom",
              "fileName": "custom.srs",
              "enabled": true,
              "action": "block",
              "builtinId": null,
              "remoteEtag": null,
              "lastDownloadedUtc": null
            }
          ],
          "localMixedPort": 2080,
          "proxyWasEnabledByUs": true,
          "previousProxySettings": {
            "proxyEnable": true,
            "proxyServer": "proxy.corp:8080",
            "proxyOverride": "<local>"
          },
          "proxyOverride": "localhost;127.*",
          "tunInterfaceName": "MyTun",
          "tunAddressCidr": "10.0.0.1/30",
          "tunMtu": 1400,
          "tunStack": "mixed",
          "tunAutoRoute": false,
          "tunStrictRoute": false,
          "dnsMode": "system",
          "dohServer": "1.1.1.1",
          "dohPath": "/q",
          "dohSni": "cloudflare-dns.com",
          "dnsDetour": "proxy",
          "debugLogs": true,
          "singBoxLogLevel": "error",
          "closeBehavior": "exit",
          "updateDismissedModalForTag": "v0.6.8",
          "lastRecordedAppSemver": "0.6.8",
          "updateLastCheckUtc": "2026-10-01T12:00:00+00:00"
        }
        """;

    private const string ProfilesJson = """
        [
          {
            "id": "a1b2c3d4e5f60718",
            "subscriptionId": "0123456789abcdef0123456789abcdef",
            "name": "Node A",
            "uuid": "11111111-1111-1111-1111-111111111111",
            "host": "node-a.example",
            "port": 443,
            "type": "ws",
            "security": "tls",
            "encryption": "none",
            "sni": "sni.example",
            "alpn": [
              "h2",
              "http/1.1"
            ],
            "fingerprint": "chrome",
            "flow": null,
            "realityPublicKey": null,
            "realityShortId": null,
            "wsPath": "/ws",
            "wsHost": "cdn.example",
            "grpcServiceName": null,
            "extraQuery": {
              "ech": "value"
            }
          },
          {
            "id": "ffeeddccbbaa9988",
            "subscriptionId": null,
            "name": "Node B",
            "uuid": "22222222-2222-2222-2222-222222222222",
            "host": "198.51.100.7",
            "port": 8443,
            "type": "grpc",
            "security": "reality",
            "encryption": "none",
            "sni": "www.example.org",
            "alpn": [],
            "fingerprint": null,
            "flow": "xtls-rprx-vision",
            "realityPublicKey": "pbk-value",
            "realityShortId": "0a1b",
            "wsPath": null,
            "wsHost": null,
            "grpcServiceName": "svc",
            "extraQuery": {}
          }
        ]
        """;

    private const string SubscriptionsJson = """
        [
          {
            "id": "0123456789abcdef0123456789abcdef",
            "name": "Provider",
            "url": "https://provider.example/sub?token=t",
            "enabled": false,
            "lastSyncUtc": "2026-10-10T08:09:10+00:00",
            "lastError": "HTTP 503",
            "updateIntervalHours": 6,
            "upload": 1024,
            "download": 2048,
            "total": 1073741824,
            "expireUtc": "2026-12-31T23:59:59+00:00"
          }
        ]
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NothingVpn.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void StateStorePort_ReadsCurrentSchema()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().StateJsonPath, StateJson);
        var before = File.ReadAllBytes(paths.Get().StateJsonPath);

        var state = new StateStorePort(paths).Load();

        Assert.Equal("a1b2c3d4e5f60718", state.ActiveProfileId);
        Assert.Equal("tun", state.Mode);
        Assert.Equal([@"C:\Program Files\App\app.exe"], state.TunAppProcessPaths);
        Assert.Equal(
            ["geosite-category-ru", "geosite-category-ru-ads", "geoip-ru", "user-ruleset-0123456789ab"],
            state.UserRuleSets.Select(r => r.Tag));
        var ru = state.UserRuleSets[0];
        Assert.Equal("Geosite: категория RU", ru.Name);
        Assert.Equal("geosite-category-ru.srs", ru.FileName);
        Assert.True(ru.Enabled);
        Assert.Equal("direct", ru.Action);
        Assert.Equal("sing-geosite:category-ru", ru.BuiltinId);
        Assert.Equal("\"abc123\"", ru.RemoteEtag);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 20, 30, TimeSpan.FromHours(3)), ru.LastDownloadedUtc);
        var custom = state.UserRuleSets[3];
        Assert.Equal("Custom", custom.Name);
        Assert.Equal("custom.srs", custom.FileName);
        Assert.True(custom.Enabled);
        Assert.Equal("block", custom.Action);
        Assert.Null(custom.BuiltinId);
        Assert.Equal(2080, state.LocalMixedPort);
        Assert.True(state.ProxyWasEnabledByUs);
        Assert.NotNull(state.PreviousProxySettings);
        Assert.True(state.PreviousProxySettings!.ProxyEnable);
        Assert.Equal("proxy.corp:8080", state.PreviousProxySettings.ProxyServer);
        Assert.Equal("<local>", state.PreviousProxySettings.ProxyOverride);
        Assert.Equal("localhost;127.*", state.ProxyOverride);
        Assert.Equal("MyTun", state.TunInterfaceName);
        Assert.Equal("10.0.0.1/30", state.TunAddressCidr);
        Assert.Equal(1400, state.TunMtu);
        Assert.Equal("mixed", state.TunStack);
        Assert.False(state.TunAutoRoute);
        Assert.False(state.TunStrictRoute);
        Assert.Equal("system", state.DnsMode);
        Assert.Equal("1.1.1.1", state.DohServer);
        Assert.Equal("/q", state.DohPath);
        Assert.Equal("cloudflare-dns.com", state.DohSni);
        Assert.Equal("proxy", state.DnsDetour);
        Assert.True(state.DebugLogs);
        Assert.Equal("error", state.SingBoxLogLevel);
        Assert.Equal("exit", state.CloseBehavior);
        Assert.Equal("v0.6.8", state.UpdateDismissedModalForTag);
        Assert.Equal("0.6.8", state.LastRecordedAppSemver);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), state.UpdateLastCheckUtc);

        // Все встроенные наборы на месте — Load не должен переписывать файл.
        Assert.Equal(before, File.ReadAllBytes(paths.Get().StateJsonPath));
    }

    [Fact]
    public void StateStorePort_MissingFieldsGetDefaultsAndBuiltinRuleSetsAreInsertedBeforeUserOnes()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().StateJsonPath, """
            {
              "activeProfileId": "p1",
              "unknownFutureField": 42,
              "userRuleSets": [
                { "tag": "user-ruleset-0123456789ab", "name": "Custom", "fileName": "custom.srs", "enabled": true, "action": "direct" }
              ]
            }
            """);

        var state = new StateStorePort(paths).Load();

        Assert.Equal("p1", state.ActiveProfileId);
        Assert.Equal("proxy", state.Mode);
        Assert.Equal(1080, state.LocalMixedPort);
        Assert.Equal("localhost;127.*;10.*;192.168.*;172.16.*", state.ProxyOverride);
        Assert.Equal("NothingVpn", state.TunInterfaceName);
        Assert.Equal("auto", state.TunAddressCidr);
        Assert.Equal(1500, state.TunMtu);
        Assert.Equal("", state.TunStack);
        Assert.True(state.TunAutoRoute);
        Assert.True(state.TunStrictRoute);
        Assert.Equal("doh", state.DnsMode);
        Assert.Equal("8.8.8.8", state.DohServer);
        Assert.Equal("/dns-query", state.DohPath);
        Assert.Equal("dns.google", state.DohSni);
        Assert.Equal("direct", state.DnsDetour);
        Assert.Equal("warn", state.SingBoxLogLevel);
        Assert.Equal("tray", state.CloseBehavior);
        Assert.False(state.ProxyWasEnabledByUs);
        Assert.Null(state.PreviousProxySettings);
        Assert.Equal(
            ["sing-geosite:category-ru", "sing-geosite:category-ru-ads", "sing-geoip:ru", null],
            state.UserRuleSets.Select(r => r.BuiltinId));
        Assert.Equal(["direct", "block", "direct", "direct"], state.UserRuleSets.Select(r => r.Action));
        Assert.All(state.UserRuleSets.Take(3), r => Assert.False(r.Enabled));

        // Встроенные наборы добавляются только в модель: Load ничего не пишет (REFACTORING 2.3).
        var reread = new StateStorePort(paths).Load();
        Assert.Equal(4, reread.UserRuleSets.Count);
        Assert.False(File.Exists(paths.Get().StateJsonPath + ".bak"));
    }

    [Fact]
    public void StateStorePort_SaveMissingBuiltinRuleSets_PersistsThemOnce()
    {
        var paths = new FakeAppPathsPort(_root);
        var path = paths.Get().StateJsonPath;
        WriteEncrypted(path, """
            {
              "userRuleSets": [
                { "tag": "user-ruleset-0123456789ab", "name": "Custom", "fileName": "custom.srs", "enabled": true, "action": "direct" }
              ]
            }
            """);
        var port = new StateStorePort(paths);

        Assert.True(port.SaveMissingBuiltinRuleSets());

        Assert.True(File.Exists(path + ".bak"));
        using var json = JsonDocument.Parse(ReadDecrypted(path));
        Assert.Equal(
            ["sing-geosite:category-ru", "sing-geosite:category-ru-ads", "sing-geoip:ru", null],
            json.RootElement.GetProperty("userRuleSets").EnumerateArray()
                .Select(r => r.GetProperty("builtinId").GetString()));

        var persisted = File.ReadAllBytes(path);
        Assert.False(port.SaveMissingBuiltinRuleSets());
        Assert.Equal(persisted, File.ReadAllBytes(path));
    }

    [Fact]
    public void ProfileStorePort_ReadsCurrentSchema()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().ProfilesJsonPath, ProfilesJson);

        var profiles = new ProfileStorePort(paths).Load();

        Assert.Equal(2, profiles.Count);
        var a = profiles[0];
        Assert.Equal("a1b2c3d4e5f60718", a.Id);
        Assert.Equal("0123456789abcdef0123456789abcdef", a.SubscriptionId);
        Assert.Equal("Node A", a.Name);
        Assert.Equal("11111111-1111-1111-1111-111111111111", a.Uuid);
        Assert.Equal("node-a.example", a.Host);
        Assert.Equal(443, a.Port);
        Assert.Equal("ws", a.Type);
        Assert.Equal("tls", a.Security);
        Assert.Equal("none", a.Encryption);
        Assert.Equal("sni.example", a.Sni);
        Assert.Equal(["h2", "http/1.1"], a.Alpn);
        Assert.Equal("chrome", a.Fingerprint);
        Assert.Null(a.Flow);
        Assert.Null(a.RealityPublicKey);
        Assert.Null(a.RealityShortId);
        Assert.Equal("/ws", a.WsPath);
        Assert.Equal("cdn.example", a.WsHost);
        Assert.Null(a.GrpcServiceName);
        Assert.Equal("value", Assert.Single(a.ExtraQuery).Value);
        Assert.Equal("value", a.ExtraQuery["ECH"]);

        var b = profiles[1];
        Assert.Equal("ffeeddccbbaa9988", b.Id);
        Assert.Null(b.SubscriptionId);
        Assert.Equal("Node B", b.Name);
        Assert.Equal("198.51.100.7", b.Host);
        Assert.Equal(8443, b.Port);
        Assert.Equal("grpc", b.Type);
        Assert.Equal("reality", b.Security);
        Assert.Equal("www.example.org", b.Sni);
        Assert.Empty(b.Alpn);
        Assert.Null(b.Fingerprint);
        Assert.Equal("xtls-rprx-vision", b.Flow);
        Assert.Equal("pbk-value", b.RealityPublicKey);
        Assert.Equal("0a1b", b.RealityShortId);
        Assert.Equal("svc", b.GrpcServiceName);
        Assert.Empty(b.ExtraQuery);
    }

    [Fact]
    public void SubscriptionStorePort_ReadsCurrentSchema()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().SubscriptionsJsonPath, SubscriptionsJson);

        var subscription = Assert.Single(new SubscriptionStorePort(paths).Load());

        Assert.Equal("0123456789abcdef0123456789abcdef", subscription.Id);
        Assert.Equal("Provider", subscription.Name);
        Assert.Equal("https://provider.example/sub?token=t", subscription.Url);
        Assert.False(subscription.Enabled);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 8, 9, 10, TimeSpan.Zero), subscription.LastSyncUtc);
        Assert.Equal("HTTP 503", subscription.LastError);
        Assert.Equal(6, subscription.UpdateIntervalHours);
        Assert.Equal(1024, subscription.UserInfo.Upload);
        Assert.Equal(2048, subscription.UserInfo.Download);
        Assert.Equal(1073741824, subscription.UserInfo.Total);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), subscription.UserInfo.ExpireUtc);
    }

    [Fact]
    public void SubscriptionStorePort_MissingFieldsGetDefaults()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().SubscriptionsJsonPath, """
            [ { "id": "s1", "name": "Old", "url": "https://old.example/sub" } ]
            """);

        var subscription = Assert.Single(new SubscriptionStorePort(paths).Load());

        Assert.True(subscription.Enabled);
        Assert.Equal(24, subscription.UpdateIntervalHours);
        Assert.Null(subscription.LastSyncUtc);
        Assert.Null(subscription.LastError);
        Assert.Equal(0, subscription.UserInfo.Total);
        Assert.Null(subscription.UserInfo.ExpireUtc);
    }

    [Fact]
    public void ProfileStorePort_ReadsLegacyPlaintextAndEncryptsIt()
    {
        var paths = new FakeAppPathsPort(_root);
        var path = paths.Get().ProfilesJsonPath;
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, ProfilesJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var profiles = new ProfileStorePort(paths).Load();

        Assert.Equal(["a1b2c3d4e5f60718", "ffeeddccbbaa9988"], profiles.Select(p => p.Id));
        Assert.Equal("NV1", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 3));
    }

    [Fact]
    public void SavedFiles_UseCurrentSchemaPropertyNames()
    {
        var paths = new FakeAppPathsPort(_root);
        new StateStorePort(paths).Save(new AppStateModel
        {
            PreviousProxySettings = new ProxySettingsSnapshotModel(),
            UserRuleSets = [new UserRuleSetModel()]
        });
        new ProfileStorePort(paths).Upsert(new VpnProfile { Id = "p" });
        new SubscriptionStorePort(paths).Upsert(new SubscriptionModel { Id = "s" });

        using var state = JsonDocument.Parse(ReadEncrypted(paths.Get().StateJsonPath));
        Assert.Equal(StateSchema, PropertyNames(state.RootElement));
        Assert.Equal(UserRuleSetSchema, PropertyNames(state.RootElement.GetProperty("userRuleSets")[0]));
        Assert.Equal(ProxySnapshotSchema, PropertyNames(state.RootElement.GetProperty("previousProxySettings")));

        using var profiles = JsonDocument.Parse(ReadEncrypted(paths.Get().ProfilesJsonPath));
        Assert.Equal(ProfileSchema, PropertyNames(profiles.RootElement[0]));

        using var subscriptions = JsonDocument.Parse(ReadEncrypted(paths.Get().SubscriptionsJsonPath));
        Assert.Equal(SubscriptionSchema, PropertyNames(subscriptions.RootElement[0]));
    }

    [Fact]
    public void StorageClasses_MatchCurrentSchema()
    {
        Assert.Equal(StateSchema, JsonPropertyNames(typeof(AppStateModel)));
        Assert.Equal(UserRuleSetSchema, JsonPropertyNames(typeof(UserRuleSetModel)));
        Assert.Equal(ProxySnapshotSchema, JsonPropertyNames(typeof(ProxySettingsSnapshotModel)));
        Assert.Equal(ProfileSchema, JsonPropertyNames(typeof(VpnProfile)));
        Assert.Equal(SubscriptionSchema, JsonPropertyNames(typeof(SubscriptionRecord)));
    }

    private static readonly string[] StateSchema =
    [
        "activeProfileId", "mode", "tunAppProcessPaths", "userRuleSets", "localMixedPort", "proxyWasEnabledByUs",
        "previousProxySettings", "proxyOverride", "tunInterfaceName", "tunAddressCidr", "tunMtu", "tunStack",
        "tunAutoRoute", "tunStrictRoute", "dnsMode", "dohServer", "dohPath", "dohSni", "dnsDetour", "debugLogs",
        "singBoxLogLevel", "closeBehavior", "updateDismissedModalForTag", "lastRecordedAppSemver", "updateLastCheckUtc"
    ];

    private static readonly string[] UserRuleSetSchema =
        ["tag", "name", "fileName", "enabled", "action", "builtinId", "remoteEtag", "lastDownloadedUtc"];

    private static readonly string[] ProxySnapshotSchema = ["proxyEnable", "proxyServer", "proxyOverride"];

    private static readonly string[] ProfileSchema =
    [
        "id", "subscriptionId", "name", "uuid", "host", "port", "type", "security", "encryption", "sni", "alpn",
        "fingerprint", "flow", "realityPublicKey", "realityShortId", "wsPath", "wsHost", "grpcServiceName", "extraQuery"
    ];

    private static readonly string[] SubscriptionSchema =
    [
        "id", "name", "url", "enabled", "lastSyncUtc", "lastError", "updateIntervalHours", "upload", "download",
        "total", "expireUtc"
    ];

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();

    // Имя, под которым свойство попадает в файл: явный [JsonPropertyName] или camelCase из DpapiJsonFile.
    private static string[] JsonPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToArray();

    private static void WriteEncrypted(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, [.. "NV1"u8, .. protectedBytes]);
    }

    private static string ReadDecrypted(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("NV1"u8.ToArray(), bytes[..3]);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            bytes[3..],
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser));
    }

    private static string ReadEncrypted(string path)
    {
        var data = File.ReadAllBytes(path);
        Assert.Equal("NV1", Encoding.ASCII.GetString(data, 0, 3));
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            data[3..],
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser));
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
