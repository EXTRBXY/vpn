using System.Collections;
using System.Reflection;
using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.Profile;
using NothingVpn.Infrastructure.Store;
using NothingVpn.Infrastructure.WinInet;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Характеризационные тесты хранения: каждое поле Application-модели проходит через LegacyModelMapper,
/// DPAPI-файл и обратно без потерь. Новое свойство модели роняет тест покрытия, пока его не заполнят здесь;
/// после этого roundtrip упадёт, если поле не проброшено через маппер или legacy-класс.
/// </summary>
public sealed class StorageRoundtripTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NothingVpn.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void StateStorePort_RoundtripsEveryField()
    {
        var port = new StateStorePort(new FakeAppPathsPort(_root));
        var state = FilledState();

        port.Save(state);
        var loaded = port.Load();

        Assert.Equal(Serialize(state), Serialize(loaded));
    }

    [Fact]
    public void ProfileStorePort_RoundtripsEveryField()
    {
        var port = new ProfileStorePort(new FakeAppPathsPort(_root));
        var profile = FilledProfile();

        port.Upsert(profile);
        var loaded = Assert.Single(port.Load());

        Assert.Equal(Serialize(profile), Serialize(loaded));
    }

    [Fact]
    public void SubscriptionStorePort_RoundtripsEveryField()
    {
        var port = new SubscriptionStorePort(new FakeAppPathsPort(_root));
        var subscription = FilledSubscription();

        port.Upsert(subscription);
        var loaded = Assert.Single(port.Load());

        Assert.Equal(Serialize(subscription), Serialize(loaded));
    }

    [Fact]
    public void FilledSamples_SetEveryPublicPropertyToNonDefaultValue()
    {
        var missing = new List<string>();
        CollectDefaultValuedProperties(FilledState(), new AppStateModel(), nameof(AppStateModel), missing);
        CollectDefaultValuedProperties(FilledProfile(), new VpnProfile(), nameof(VpnProfile), missing);
        CollectDefaultValuedProperties(FilledSubscription(), new SubscriptionModel(), nameof(SubscriptionModel), missing);

        Assert.True(
            missing.Count == 0,
            "Fill these properties with unique non-default values in StorageRoundtripTests: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void ApplicationModels_HaveSameFieldsAsStorageClasses()
    {
        AssertSameProperties<AppStateModel, AppState>();
        AssertSameProperties<UserRuleSetModel, UserRuleSet>();
        AssertSameProperties<ProxySettingsSnapshotModel, WinInetProxySettingsSnapshot>();
        AssertSameProperties<VpnProfile, VlessProfile>();

        // SubscriptionRecord хранит UserInfo плоско.
        var modelNames = PropertyNames(typeof(SubscriptionModel))
            .Where(n => n != nameof(SubscriptionModel.UserInfo))
            .Concat(PropertyNames(typeof(SubscriptionUserInfoModel)))
            .Order(StringComparer.Ordinal);
        Assert.Equal(modelNames, PropertyNames(typeof(SubscriptionRecord)).Order(StringComparer.Ordinal));
    }

    private static AppStateModel FilledState() => new()
    {
        ActiveProfileId = "state-active-profile",
        Mode = "tun_apps",
        TunAppProcessPaths = [@"C:\Apps\one.exe", @"D:\Apps\two.exe"],
        UserRuleSets =
        [
            // Все встроенные наборы присутствуют, иначе Load допишет их и сравнение станет неочевидным.
            new UserRuleSetModel
            {
                Tag = "geosite-category-ru",
                Name = "rs-name-1",
                FileName = "geosite-category-ru.srs",
                Enabled = false,
                Action = "block",
                BuiltinId = "sing-geosite:category-ru",
                RemoteEtag = "\"etag-1\"",
                LastDownloadedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3))
            },
            new UserRuleSetModel
            {
                Tag = "geosite-category-ru-ads",
                Name = "rs-name-2",
                FileName = "geosite-category-ru@ads.srs",
                Enabled = true,
                Action = "block",
                BuiltinId = "sing-geosite:category-ru-ads",
                RemoteEtag = "\"etag-2\"",
                LastDownloadedUtc = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero)
            },
            new UserRuleSetModel
            {
                Tag = "geoip-ru",
                Name = "rs-name-3",
                FileName = "geoip-ru.srs",
                Enabled = true,
                Action = "direct",
                BuiltinId = "sing-geoip:ru"
            },
            new UserRuleSetModel
            {
                Tag = "user-ruleset-aaaaaaaaaaaa",
                Name = "rs-name-4",
                FileName = "user-list.srs",
                Enabled = false,
                Action = "block"
            }
        ],
        LocalMixedPort = 21080,
        ProxyWasEnabledByUs = true,
        PreviousProxySettings = new ProxySettingsSnapshotModel
        {
            ProxyEnable = true,
            ProxyServer = "corp-proxy:3128",
            ProxyOverride = "<local>;intranet.*"
        },
        ProxyOverride = "localhost;10.*",
        TunInterfaceName = "StateIface",
        TunAddressCidr = "10.123.0.1/30",
        TunMtu = 1400,
        TunStack = "gvisor",
        TunAutoRoute = false,
        TunStrictRoute = false,
        DnsMode = "system",
        DohServer = "9.9.9.9",
        DohPath = "/state-dns-query",
        DohSni = "dns.quad9.net",
        DnsDetour = "proxy",
        DebugLogs = true,
        SingBoxLogLevel = "debug",
        CloseBehavior = AppCloseBehavior.Exit,
        UpdateDismissedModalForTag = "v9.8.7",
        LastRecordedAppSemver = "1.2.3",
        UpdateLastCheckUtc = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-5))
    };

    private static VpnProfile FilledProfile() => new()
    {
        Id = "profile-id-0001",
        SubscriptionId = "profile-subscription-id",
        Name = "profile-name",
        Uuid = "12345678-9abc-def0-1234-56789abcdef0",
        Host = "profile-host.example",
        Port = 8443,
        Type = "grpc",
        Security = "reality",
        Encryption = "profile-encryption",
        Sni = "profile-sni.example",
        Alpn = ["h2", "http/1.1"],
        Fingerprint = "profile-fp",
        Flow = "profile-flow",
        RealityPublicKey = "profile-pbk",
        RealityShortId = "profile-sid",
        WsPath = "/profile-ws-path",
        WsHost = "profile-ws-host.example",
        GrpcServiceName = "profile-grpc-service",
        ExtraQuery = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ech"] = "profile-ech",
            ["custom"] = "profile-custom"
        }
    };

    private static SubscriptionModel FilledSubscription() => new()
    {
        Id = "subscription-id-0001",
        Name = "subscription-name",
        Url = "https://subscription.example/path?token=abc",
        Enabled = false,
        LastSyncUtc = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.FromHours(7)),
        LastError = "subscription-last-error",
        UpdateIntervalHours = 12,
        UserInfo = new SubscriptionUserInfoModel
        {
            Upload = 1_111_111_111,
            Download = 2_222_222_222,
            Total = 3_333_333_333,
            ExpireUtc = new DateTimeOffset(2027, 5, 6, 7, 8, 9, TimeSpan.Zero)
        }
    };

    private static void CollectDefaultValuedProperties(object filled, object defaults, string path, List<string> missing)
    {
        foreach (var property in PublicProperties(filled.GetType()))
        {
            var name = $"{path}.{property.Name}";
            var filledValue = property.GetValue(filled);
            var defaultValue = property.GetValue(defaults);

            if (filledValue is null)
            {
                missing.Add(name);
                continue;
            }

            var type = property.PropertyType;
            if (IsNestedModel(type))
            {
                CollectDefaultValuedProperties(filledValue, Activator.CreateInstance(type)!, name, missing);
                continue;
            }

            if (filledValue is IEnumerable items and not string)
            {
                var list = items.Cast<object>().ToList();
                if (list.Count == 0)
                {
                    missing.Add(name);
                    continue;
                }

                var elementType = list[0].GetType();
                if (IsNestedModel(elementType))
                {
                    var template = Activator.CreateInstance(elementType)!;
                    foreach (var elementProperty in PublicProperties(elementType))
                    {
                        if (!list.Any(item => !IsSameValue(elementProperty.GetValue(item), elementProperty.GetValue(template))))
                            missing.Add($"{name}[].{elementProperty.Name}");
                    }
                }

                continue;
            }

            if (IsSameValue(filledValue, defaultValue))
                missing.Add(name);
        }
    }

    private static bool IsNestedModel(Type type) =>
        type.IsClass && type != typeof(string) && type.Namespace == typeof(AppStateModel).Namespace;

    private static bool IsSameValue(object? left, object? right) => Equals(left, right);

    private static void AssertSameProperties<TModel, TStorage>() =>
        Assert.Equal(
            PropertyNames(typeof(TModel)).Order(StringComparer.Ordinal),
            PropertyNames(typeof(TStorage)).Order(StringComparer.Ordinal));

    private static IEnumerable<string> PropertyNames(Type type) => PublicProperties(type).Select(p => p.Name);

    private static IEnumerable<PropertyInfo> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });

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
