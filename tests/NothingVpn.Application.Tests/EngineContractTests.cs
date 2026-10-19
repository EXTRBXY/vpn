using System.Text.Json;
using System.Text.Json.Nodes;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Контракт ядра — будущий протокол канала службы (шаг 7): запрос и статус переживают System.Text.Json без потерь,
/// а конфиг sing-box, построенный из запроса, совпадает с конфигом из state.json напрямую (эталоны генератора).
/// </summary>
public sealed class EngineContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ContractVersion_IsPinned()
    {
        // Смена версии — осознанное несовместимое изменение протокола: обновите тест и ADR-0001.
        Assert.Equal(1, EngineContract.Version);
        Assert.Equal(EngineContract.Version, new EngineConnectRequest().ContractVersion);
        Assert.Equal(EngineContract.Version, new EngineStatus().ContractVersion);
    }

    [Fact]
    public void ConnectRequest_RoundtripsThroughJson()
    {
        var request = new EngineConnectRequest
        {
            Profile = new EngineProfile
            {
                Id = "0123456789abcdef",
                Uuid = "11111111-2222-3333-4444-555555555555",
                Host = "node.example.com",
                Port = 8443,
                Type = "ws",
                Security = "reality",
                Sni = "www.example.org",
                Alpn = ["h2", "http/1.1"],
                Fingerprint = "firefox",
                Flow = "xtls-rprx-vision",
                RealityPublicKey = "pbk",
                RealityShortId = "a1b2",
                WsPath = "/ws",
                WsHost = "cdn.example.net",
                GrpcServiceName = "grpc"
            },
            Mode = ConnectionMode.TunApps,
            LogLevel = LogLevel.Debug,
            LocalMixedPort = 2080,
            Dns = new EngineDnsSettings
            {
                Mode = DnsMode.System,
                DohServer = "1.1.1.1",
                DohPath = "/q",
                DohSni = "one.one.one.one",
                Detour = DnsDetour.Proxy
            },
            Tun = new EngineTunSettings
            {
                InterfaceName = "Tun0",
                AddressCidr = "10.10.0.1/30",
                Mtu = 1400,
                Stack = TunStack.Gvisor,
                AutoRoute = false,
                StrictRoute = false
            },
            TunAppProcessPaths = [@"C:\Apps\browser.exe"],
            RuleSets = [new EngineRuleSet { Tag = "ads", FileName = "ads.srs", Action = RuleSetAction.Block }]
        };

        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<EngineConnectRequest>(json)!;

        Assert.Equal(json, JsonSerializer.Serialize(restored));
        var node = JsonNode.Parse(json)!;
        Assert.Equal(1, (int)node["contractVersion"]!);
        Assert.Equal("tun_apps", (string?)node["mode"]);
        Assert.Equal("debug", (string?)node["logLevel"]);
        Assert.Equal("system", (string?)node["dns"]!["mode"]);
        Assert.Equal("proxy", (string?)node["dns"]!["detour"]);
        Assert.Equal("gvisor", (string?)node["tun"]!["stack"]);
        Assert.Equal("block", (string?)node["ruleSets"]![0]!["action"]);
        Assert.Equal("ws", restored.Profile.Type);
        Assert.Equal(["h2", "http/1.1"], restored.Profile.Alpn);
    }

    [Fact]
    public void ConnectRequest_CarriesNoConfigOrWritablePaths()
    {
        var names = typeof(EngineConnectRequest).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(EngineConnectRequest).Namespace && t.Name.StartsWith("Engine", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties())
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(names, n => n.Contains("Json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Dir", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("ConfigPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Status_RoundtripsThroughJson()
    {
        var status = new EngineStatus { IsRunning = true, Mode = ConnectionMode.Tun, ProfileId = "p1" };

        var json = JsonSerializer.Serialize(status);
        var restored = JsonSerializer.Deserialize<EngineStatus>(json)!;

        Assert.Contains("\"mode\":\"tun\"", json, StringComparison.Ordinal);
        Assert.True(restored.IsRunning);
        Assert.Equal(ConnectionMode.Tun, restored.Mode);
        Assert.Equal("p1", restored.ProfileId);
        Assert.Equal(EngineContract.Version, restored.ContractVersion);
    }

    [Theory]
    [MemberData(nameof(SingBoxConfigGoldenTests.Matrix), MemberType = typeof(SingBoxConfigGoldenTests))]
    public void ConfigBuiltFromRequest_MatchesConfigBuiltFromState(string mode, string dnsMode, string detour, bool ruleSets)
    {
        var state = SingBoxConfigGoldenTests.CreateState(mode, dnsMode, detour, ruleSets);

        AssertSameConfig(SingBoxConfigGoldenTests.RealityProfile(), state);
    }

    [Fact]
    public void ConfigBuiltFromRequest_MatchesForEveryTransport()
    {
        foreach (var mode in new[] { "proxy", "tun" })
        {
            AssertSameConfig(SingBoxConfigGoldenTests.TlsWebSocketProfile(), SingBoxConfigGoldenTests.CreateState(mode, "doh", "direct", false));
            AssertSameConfig(SingBoxConfigGoldenTests.GrpcWithoutTlsProfile(), SingBoxConfigGoldenTests.CreateState(mode, "system", "direct", false));
        }
    }

    private void AssertSameConfig(VpnProfile profile, AppStateModel state)
    {
        var pathPolicy = new PathPolicyPort();
        AppStateNormalizer.Normalize(state, pathPolicy);
        var direct = File.ReadAllText(SingBoxConfigGenerator.WriteConfig(CreatePaths("direct"), profile, state));

        // Путь запроса целиком: интерфейс → JSON канала → ядро (нормализация на приёме) → генератор.
        var wire = JsonSerializer.Serialize(EngineRequestMapper.CreateRequest(profile, state));
        var request = JsonSerializer.Deserialize<EngineConnectRequest>(wire)!;
        var engineState = EngineRequestMapper.ToState(request);
        AppStateNormalizer.Normalize(engineState, pathPolicy);
        var viaEngine = File.ReadAllText(SingBoxConfigGenerator.WriteConfig(
            CreatePaths("engine"),
            EngineRequestMapper.ToProfile(request),
            engineState));

        Assert.Equal(direct, viaEngine);
    }

    private AppPaths CreatePaths(string name)
    {
        var configs = Path.Combine(_root, name);
        Directory.CreateDirectory(configs);
        return new AppPaths(
            BaseDir: @"C:\NothingVpnGolden",
            ConfigsDir: configs,
            RuleSetsDir: @"C:\NothingVpnGolden\rulesets",
            LogsDir: @"C:\NothingVpnGolden\logs",
            ProfilesJsonPath: @"C:\NothingVpnGolden\profiles.json",
            SubscriptionsJsonPath: @"C:\NothingVpnGolden\subscriptions.json",
            StateJsonPath: @"C:\NothingVpnGolden\state.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
