using NothingVpn.Domain.Policies;
using System.Runtime.CompilerServices;
using NothingVpn.Domain.Models;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Характеризационные тесты: итоговый JSON sing-box сравнивается целиком с эталонами в Golden/SingBox.
/// Эталоны сгенерированы текущим кодом. Чтобы осознанно обновить их, запустите тесты с
/// NOTHINGVPN_UPDATE_GOLDEN=1 — файлы перезапишутся в исходном каталоге проекта; изменения проверьте в diff.
/// </summary>
public sealed class SingBoxConfigGoldenTests : IDisposable
{
    private const string UpdateGoldenVariable = "NOTHINGVPN_UPDATE_GOLDEN";
    private const string FixedRuleSetsDir = @"C:\NothingVpnGolden\rulesets";

    private readonly string _configsDir = Path.Combine(
        Path.GetTempPath(),
        "NothingVpn.Tests",
        Guid.NewGuid().ToString("N"),
        "configs");

    public static TheoryData<string, string, string, bool> Matrix()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var mode in new[] { "proxy", "tun", "tun_apps" })
        foreach (var dnsMode in new[] { "doh", "system" })
        foreach (var detour in new[] { "direct", "proxy" })
        foreach (var ruleSets in new[] { false, true })
            data.Add(mode, dnsMode, detour, ruleSets);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void WriteConfig_MatchesGolden(string mode, string dnsMode, string detour, bool ruleSets)
    {
        var name = $"{mode}.{dnsMode}.{detour}.{(ruleSets ? "rulesets" : "norulesets")}";
        var state = CreateState(mode, dnsMode, detour, ruleSets);

        AssertMatchesGolden(name, RealityProfile(), state);
    }

    [Theory]
    [InlineData("proxy")]
    [InlineData("tun")]
    public void WriteConfig_TlsWebSocketProfile_MatchesGolden(string mode)
    {
        var state = CreateState(mode, "doh", "direct", ruleSets: false);

        AssertMatchesGolden($"profile-tls-ws.{mode}", TlsWebSocketProfile(), state);
    }

    [Theory]
    [InlineData("proxy")]
    [InlineData("tun")]
    public void WriteConfig_GrpcWithoutTlsProfile_MatchesGolden(string mode)
    {
        var state = CreateState(mode, "system", "direct", ruleSets: false);

        AssertMatchesGolden($"profile-grpc-none.{mode}", GrpcWithoutTlsProfile(), state);
    }

    private void AssertMatchesGolden(string name, VpnProfile profile, AppStateModel state)
    {
        // Тот же путь, что при подключении: VpnConnectionService нормализует состояние перед генерацией.
        AppStateNormalizer.Normalize(state, new PathPolicyPort());
        var actual = Normalize(File.ReadAllText(SingBoxConfigGenerator.WriteConfig(CreatePaths(), profile, state)));

        if (Environment.GetEnvironmentVariable(UpdateGoldenVariable) == "1")
        {
            var sourcePath = Path.Combine(SourceGoldenDir(), name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual);
            return;
        }

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Golden", "SingBox", name + ".json");
        Assert.True(File.Exists(goldenPath), $"Golden file is missing: {goldenPath}. Run tests with {UpdateGoldenVariable}=1.");
        var expected = Normalize(File.ReadAllText(goldenPath));

        Assert.Equal(expected, actual);
    }

    private AppPaths CreatePaths()
    {
        Directory.CreateDirectory(_configsDir);
        return new AppPaths(
            BaseDir: @"C:\NothingVpnGolden",
            ConfigsDir: _configsDir,
            RuleSetsDir: FixedRuleSetsDir,
            LogsDir: @"C:\NothingVpnGolden\logs",
            ProfilesJsonPath: @"C:\NothingVpnGolden\profiles.json",
            SubscriptionsJsonPath: @"C:\NothingVpnGolden\subscriptions.json",
            StateJsonPath: @"C:\NothingVpnGolden\state.json");
    }

    internal static AppStateModel CreateState(string mode, string dnsMode, string detour, bool ruleSets) => new()
    {
        ActiveProfileId = "0123456789abcdef",
        Mode = ConnectionPolicy.Modes.Parse(mode),
        TunAppProcessPaths =
        [
            @"C:\Program Files\Browser\browser.exe",
            @"D:\Games\Client\game.exe"
        ],
        UserRuleSets =
        [
            new UserRuleSetModel
            {
                BuiltinId = "sing-geosite:category-ru",
                Tag = "geosite-category-ru",
                Name = "Geosite: категория RU",
                FileName = "geosite-category-ru.srs",
                Enabled = ruleSets,
                Action = RuleSetAction.Direct
            },
            new UserRuleSetModel
            {
                BuiltinId = "sing-geosite:category-ru-ads",
                Tag = "geosite-category-ru-ads",
                Name = "Geosite: категория RU — реклама",
                FileName = "geosite-category-ru@ads.srs",
                Enabled = ruleSets,
                Action = RuleSetAction.Block
            },
            new UserRuleSetModel
            {
                BuiltinId = "sing-geoip:ru",
                Tag = "geoip-ru",
                Name = "GeoIP: RU",
                FileName = "geoip-ru.srs",
                Enabled = ruleSets,
                Action = RuleSetAction.Direct
            },
            new UserRuleSetModel
            {
                Tag = "user-ruleset-0123456789ab",
                Name = "My list",
                FileName = "my-list.srs",
                Enabled = ruleSets,
                Action = RuleSetAction.Direct
            }
        ],
        LocalMixedPort = 1080,
        TunInterfaceName = "NothingVpn",
        TunAddressCidr = "auto",
        TunMtu = 1500,
        TunStack = TunStack.Default,
        TunAutoRoute = true,
        TunStrictRoute = true,
        DnsMode = DnsPolicy.Modes.Parse(dnsMode),
        DohServer = "8.8.8.8",
        DohPath = "/dns-query",
        DohSni = "dns.google",
        DnsDetour = DnsPolicy.Detours.Parse(detour),
        SingBoxLogLevel = LogLevel.Warn
    };

    internal static VpnProfile RealityProfile() => new()
    {
        Id = "0123456789abcdef",
        Name = "Golden reality",
        Uuid = "11111111-2222-3333-4444-555555555555",
        Host = "node.example.com",
        Port = 443,
        Type = "tcp",
        Security = "reality",
        Encryption = "none",
        Sni = "www.example.org",
        Fingerprint = "chrome",
        Flow = "xtls-rprx-vision",
        RealityPublicKey = "pUbLiCkEyAbCdEfGhIjKlMnOpQrStUvWxYz0123456",
        RealityShortId = "a1b2c3d4"
    };

    internal static VpnProfile TlsWebSocketProfile() => new()
    {
        Id = "fedcba9876543210",
        Name = "Golden ws",
        Uuid = "66666666-7777-8888-9999-aaaaaaaaaaaa",
        Host = "ws.example.net",
        Port = 8443,
        Type = "ws",
        Security = "tls",
        Encryption = "none",
        Sni = "cdn.example.net",
        Alpn = ["h2", "http/1.1"],
        Fingerprint = "firefox",
        WsPath = "/ws-path",
        WsHost = "cdn.example.net"
    };

    internal static VpnProfile GrpcWithoutTlsProfile() => new()
    {
        Id = "00ff00ff00ff00ff",
        Name = "Golden grpc",
        Uuid = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
        Host = "203.0.113.10",
        Port = 2053,
        Type = "grpc",
        Security = "none",
        Encryption = "none",
        GrpcServiceName = "grpc-service"
    };

    // Git на Windows может переписать концы строк в эталонах (core.autocrlf), поэтому сравниваем с LF.
    private static string Normalize(string json) => json.Replace("\r\n", "\n");

    private static string SourceGoldenDir([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden", "SingBox");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_configsDir)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
