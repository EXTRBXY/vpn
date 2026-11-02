using System.Text.Json.Nodes;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;
using NothingVpn.Infrastructure.Xray;

namespace NothingVpn.Application.Tests;

/// <summary>Конфиг Xray-выхода и сторона sing-box при внешнем движке (ADR-0002).</summary>
public sealed class XrayConfigGeneratorTests : IDisposable
{
    private static readonly LocalSocksUpstream Upstream =
        new(41000, "0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210", @"C:\Program Files\NothingVpn\xray.exe");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Xray_HasPasswordSocksInboundOnLoopbackOnly()
    {
        var config = Xray(XhttpRealityProfile(), State());

        var inbound = config["inbounds"]!.AsArray().Single()!;
        Assert.Equal("socks", (string?)inbound["protocol"]);
        Assert.Equal("127.0.0.1", (string?)inbound["listen"]);
        Assert.Equal(41000, (int)inbound["port"]!);
        Assert.Equal("password", (string?)inbound["settings"]!["auth"]);
        Assert.True((bool)inbound["settings"]!["udp"]!);
        var account = inbound["settings"]!["accounts"]!.AsArray().Single()!;
        Assert.Equal(Upstream.Username, (string?)account["user"]);
        Assert.Equal(Upstream.Password, (string?)account["pass"]);
        Assert.Equal("none", (string?)config["log"]!["access"]);
    }

    [Fact]
    public void Xray_BuildsXhttpRealityOutbound()
    {
        var config = Xray(XhttpRealityProfile(), State());

        var outbound = config["outbounds"]!.AsArray().Single()!;
        Assert.Equal("vless", (string?)outbound["protocol"]);
        Assert.Equal("proxy", (string?)outbound["tag"]);
        var settings = outbound["settings"]!;
        Assert.Equal("node.example.com", (string?)settings["address"]);
        Assert.Equal(443, (int)settings["port"]!);
        Assert.Equal("none", (string?)settings["encryption"]);
        Assert.Null(settings["flow"]);

        var stream = outbound["streamSettings"]!;
        Assert.Equal("xhttp", (string?)stream["network"]);
        Assert.Equal("reality", (string?)stream["security"]);
        Assert.Equal("www.example.org", (string?)stream["realitySettings"]!["serverName"]);
        Assert.Equal("pbk", (string?)stream["realitySettings"]!["publicKey"]);
        Assert.Equal("a1b2", (string?)stream["realitySettings"]!["shortId"]);
        Assert.Null(stream["tlsSettings"]);
        var xhttp = stream["xhttpSettings"]!;
        Assert.Equal("/xh", (string?)xhttp["path"]);
        Assert.Equal("stream-up", (string?)xhttp["mode"]);
        Assert.Equal("100-1000", (string?)xhttp["extra"]!["xPaddingBytes"]);
    }

    [Fact]
    public void Xray_WritesXmuxDefaultsFromSettingsExplicitly()
    {
        var state = State();
        state.XhttpXmuxMaxConcurrency = "0";
        state.XhttpXmuxMaxConnections = "4";

        var xmux = Xray(XhttpRealityProfile(), state)["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["extra"]!["xmux"]!;

        Assert.Equal("0", (string?)xmux["maxConcurrency"]);
        Assert.Equal("4", (string?)xmux["maxConnections"]);
        Assert.Equal(XhttpPolicy.DefaultXmuxCMaxReuseTimes, (string?)xmux["cMaxReuseTimes"]);
        Assert.Equal(XhttpPolicy.DefaultXmuxHMaxRequestTimes, (string?)xmux["hMaxRequestTimes"]);
        Assert.Equal(XhttpPolicy.DefaultXmuxHMaxReusableSecs, (string?)xmux["hMaxReusableSecs"]);
    }

    [Fact]
    public void Xray_PrefersXmuxFromLinkAndDoesNotMutateProfile()
    {
        var profile = XhttpRealityProfile();
        profile.XhttpExtra!.Xmux = new XmuxModel { MaxConnections = "2" };

        var xmux = Xray(profile, State())["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["extra"]!["xmux"]!;

        Assert.Equal("2", (string?)xmux["maxConnections"]);
        Assert.Null(xmux["maxConcurrency"]);
        Assert.Equal("2", profile.XhttpExtra.Xmux.MaxConnections);
        Assert.Null(profile.XhttpExtra.Xmux.MaxConcurrency);
    }

    [Fact]
    public void Xray_MapsTcpToRawAndKeepsVisionOnlyThere()
    {
        var tcp = SingBoxConfigGoldenTests.RealityProfile();
        var ws = SingBoxConfigGoldenTests.TlsWebSocketProfile();
        ws.Flow = "xtls-rprx-vision";

        var tcpOutbound = Xray(tcp, State())["outbounds"]![0]!;
        var wsOutbound = Xray(ws, State())["outbounds"]![0]!;

        Assert.Equal("raw", (string?)tcpOutbound["streamSettings"]!["network"]);
        Assert.Equal("xtls-rprx-vision", (string?)tcpOutbound["settings"]!["flow"]);
        Assert.Null(wsOutbound["settings"]!["flow"]);
        Assert.Equal("/ws-path", (string?)wsOutbound["streamSettings"]!["wsSettings"]!["path"]);
        Assert.Equal("firefox", (string?)wsOutbound["streamSettings"]!["tlsSettings"]!["fingerprint"]);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "debug")]
    [InlineData(LogLevel.Info, "info")]
    [InlineData(LogLevel.Warn, "warning")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Panic, "error")]
    public void Xray_MapsLogLevel(LogLevel level, string expected)
    {
        var state = State();
        state.SingBoxLogLevel = level;

        Assert.Equal(expected, (string?)Xray(XhttpRealityProfile(), state)["log"]!["loglevel"]);
    }

    [Fact]
    public void Upstream_DoesNotPrintCredentials()
    {
        var text = Upstream.ToString();

        Assert.DoesNotContain(Upstream.Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Upstream.Username, text, StringComparison.Ordinal);
    }

    [Fact]
    public void SingBox_UsesLocalSocksAsProxyOutbound()
    {
        var state = SingBoxConfigGoldenTests.CreateState("proxy", "doh", "proxy", ruleSets: false);

        var proxy = SingBox(XhttpRealityProfile(), state)["outbounds"]![0]!;

        Assert.Equal("socks", (string?)proxy["type"]);
        Assert.Equal("proxy", (string?)proxy["tag"]);
        Assert.Equal("127.0.0.1", (string?)proxy["server"]);
        Assert.Equal(41000, (int)proxy["server_port"]!);
        Assert.Equal("5", (string?)proxy["version"]);
        Assert.Equal(Upstream.Username, (string?)proxy["username"]);
        Assert.Equal(Upstream.Password, (string?)proxy["password"]);
        Assert.Null(proxy["uuid"]);
        Assert.Null(proxy["tls"]);
        Assert.Null(proxy["domain_resolver"]);
    }

    [Fact]
    public void SingBox_ExcludesXrayAndServerIpFromTun()
    {
        var profile = XhttpRealityProfile();
        profile.Host = "203.0.113.7";
        var state = SingBoxConfigGoldenTests.CreateState("tun", "doh", "direct", ruleSets: false);

        var rules = SingBox(profile, state)["route"]!["rules"]!.AsArray();

        var processRule = IndexOf(rules, r => r["process_path"]?[0]?.GetValue<string>() == Upstream.ProcessPath);
        var ipRule = IndexOf(rules, r => r["ip_cidr"]?[0]?.GetValue<string>() == "203.0.113.7/32");
        Assert.True(processRule > 0);
        Assert.Equal(processRule + 1, ipRule);
        Assert.Equal("direct", (string?)rules[processRule]!["outbound"]);
        Assert.Equal("direct", (string?)rules[ipRule]!["outbound"]);
        Assert.True(processRule > IndexOf(rules, r => (string?)r["action"] == "hijack-dns"));
    }

    [Fact]
    public void SingBox_WithoutUpstreamHasNoXrayExclusions()
    {
        var state = SingBoxConfigGoldenTests.CreateState("tun", "doh", "direct", ruleSets: false);
        AppStateNormalizer.Normalize(state, new PathPolicyPort());

        var json = JsonNode.Parse(File.ReadAllText(
            SingBoxConfigGenerator.WriteConfig(Paths("plain"), SingBoxConfigGoldenTests.GrpcWithoutTlsProfile(), state)))!;

        var rules = json["route"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r!["process_path"] is not null);
        Assert.DoesNotContain(rules, r => r!["ip_cidr"]?.ToJsonString().Contains("203.0.113.10/32") == true);
        Assert.Equal("vless", (string?)json["outbounds"]![0]!["type"]);
    }

    private JsonNode SingBox(VpnProfile profile, AppStateModel state)
    {
        AppStateNormalizer.Normalize(state, new PathPolicyPort());
        return JsonNode.Parse(File.ReadAllText(SingBoxConfigGenerator.WriteConfig(Paths("singbox"), profile, state, Upstream)))!;
    }

    private static JsonNode Xray(VpnProfile profile, AppStateModel state) =>
        JsonNode.Parse(XrayConfigGenerator.Serialize(profile, state, Upstream))!;

    private static int IndexOf(JsonArray rules, Func<JsonNode, bool> predicate)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (predicate(rules[i]!))
                return i;
        }
        return -1;
    }

    private static AppStateModel State() => new() { SingBoxLogLevel = LogLevel.Warn };

    private static VpnProfile XhttpRealityProfile() => new()
    {
        Id = "0123456789abcdef",
        Uuid = "11111111-2222-3333-4444-555555555555",
        Host = "node.example.com",
        Port = 443,
        Type = "xhttp",
        Security = "reality",
        Sni = "www.example.org",
        RealityPublicKey = "pbk",
        RealityShortId = "a1b2",
        XhttpPath = "/xh",
        XhttpMode = XhttpMode.StreamUp,
        XhttpExtra = new XhttpExtraModel { XPaddingBytes = "100-1000" }
    };

    private AppPaths Paths(string name)
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
