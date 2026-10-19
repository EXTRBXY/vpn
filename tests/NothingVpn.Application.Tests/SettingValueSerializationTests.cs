using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Enum-настройки хранятся в state.json прежними строками. Неизвестное или битое значение не должно делать файл
/// нечитаемым: иначе DpapiJsonFile откатится на .bak или пустые настройки и пользователь потеряет всё.
/// </summary>
public sealed class SettingValueSerializationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void StateStorePort_UnknownValues_FallBackPerField_AndKeepOtherSettings()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().StateJsonPath, """
            {
              "activeProfileId": "p1",
              "mode": "tun_turbo",
              "tunStack": "lwip",
              "dnsMode": "dot",
              "dohServer": "9.9.9.9",
              "dnsDetour": "vpn",
              "singBoxLogLevel": "verbose",
              "closeBehavior": "minimize",
              "proxyOverride": "localhost",
              "userRuleSets": [
                { "tag": "user-ruleset-0123456789ab", "name": "Custom", "fileName": "custom.srs", "enabled": true, "action": "reject" }
              ]
            }
            """);

        var state = new StateStorePort(paths).Load();

        Assert.Equal(ConnectionMode.Proxy, state.Mode);
        Assert.Equal(TunStack.Default, state.TunStack);
        Assert.Equal(DnsMode.Doh, state.DnsMode);
        Assert.Equal(DnsDetour.Direct, state.DnsDetour);
        // Как до перехода на enum: неизвестный уровень даёт info, а не значение по умолчанию warn.
        Assert.Equal(LogLevel.Info, state.SingBoxLogLevel);
        Assert.Equal(AppCloseBehavior.HideToTray, state.CloseBehavior);
        var custom = Assert.Single(state.UserRuleSets, r => r.Tag == "user-ruleset-0123456789ab");
        Assert.Equal(RuleSetAction.Direct, custom.Action);
        Assert.Equal("Custom", custom.Name);
        Assert.True(custom.Enabled);

        Assert.Equal("p1", state.ActiveProfileId);
        Assert.Equal("9.9.9.9", state.DohServer);
        Assert.Equal("localhost", state.ProxyOverride);
        Assert.False(File.Exists(paths.Get().StateJsonPath + ".bak"));
    }

    [Fact]
    public void StateStorePort_ReadsValuesCaseInsensitivelyAndTrimmed()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().StateJsonPath, """
            {
              "mode": " TUN_APPS ",
              "tunStack": "GVisor",
              "dnsMode": "SYSTEM",
              "dnsDetour": "Proxy",
              "singBoxLogLevel": "Warning",
              "closeBehavior": "EXIT",
              "userRuleSets": [ { "tag": "t", "fileName": "t.srs", "action": "BLOCK" } ]
            }
            """);

        var state = new StateStorePort(paths).Load();

        Assert.Equal(ConnectionMode.TunApps, state.Mode);
        Assert.Equal(TunStack.Gvisor, state.TunStack);
        Assert.Equal(DnsMode.System, state.DnsMode);
        Assert.Equal(DnsDetour.Proxy, state.DnsDetour);
        Assert.Equal(LogLevel.Warn, state.SingBoxLogLevel);
        Assert.Equal(AppCloseBehavior.Exit, state.CloseBehavior);
        Assert.Equal(RuleSetAction.Block, Assert.Single(state.UserRuleSets, r => r.Tag == "t").Action);
    }

    [Fact]
    public void StateStorePort_NullAndNonStringValues_FallBackInsteadOfFailing()
    {
        var paths = new FakeAppPathsPort(_root);
        WriteEncrypted(paths.Get().StateJsonPath, """
            {
              "activeProfileId": "p1",
              "mode": 1,
              "tunStack": null,
              "dnsMode": { "value": "system" },
              "dnsDetour": ["proxy"],
              "singBoxLogLevel": null,
              "closeBehavior": true,
              "tunMtu": 1400,
              "userRuleSets": [ { "tag": "t", "fileName": "t.srs", "action": null } ]
            }
            """);

        var state = new StateStorePort(paths).Load();

        Assert.Equal("p1", state.ActiveProfileId);
        Assert.Equal(1400, state.TunMtu);
        Assert.Equal(ConnectionMode.Proxy, state.Mode);
        Assert.Equal(TunStack.Default, state.TunStack);
        Assert.Equal(DnsMode.Doh, state.DnsMode);
        Assert.Equal(DnsDetour.Direct, state.DnsDetour);
        Assert.Equal(LogLevel.Info, state.SingBoxLogLevel);
        Assert.Equal(AppCloseBehavior.HideToTray, state.CloseBehavior);
        Assert.Equal(RuleSetAction.Direct, Assert.Single(state.UserRuleSets, r => r.Tag == "t").Action);
        Assert.False(File.Exists(paths.Get().StateJsonPath + ".bak"));
    }

    [Fact]
    public void StateStorePort_WritesCanonicalStrings()
    {
        var paths = new FakeAppPathsPort(_root);
        new StateStorePort(paths).Save(new AppStateModel
        {
            Mode = ConnectionMode.TunApps,
            TunStack = TunStack.Default,
            DnsMode = DnsMode.System,
            DnsDetour = DnsDetour.Proxy,
            SingBoxLogLevel = LogLevel.Error,
            CloseBehavior = AppCloseBehavior.Exit,
            UserRuleSets = [new UserRuleSetModel { Tag = "t", FileName = "t.srs", Action = RuleSetAction.Block }]
        });

        using var json = JsonDocument.Parse(ReadEncrypted(paths.Get().StateJsonPath));
        var root = json.RootElement;
        Assert.Equal("tun_apps", root.GetProperty("mode").GetString());
        Assert.Equal("", root.GetProperty("tunStack").GetString());
        Assert.Equal("system", root.GetProperty("dnsMode").GetString());
        Assert.Equal("proxy", root.GetProperty("dnsDetour").GetString());
        Assert.Equal("error", root.GetProperty("singBoxLogLevel").GetString());
        Assert.Equal("exit", root.GetProperty("closeBehavior").GetString());
        Assert.Equal("block", root.GetProperty("userRuleSets")[0].GetProperty("action").GetString());
    }

    [Fact]
    public void Serializer_UndefinedEnumValue_IsWrittenAsFallback()
    {
        var json = JsonSerializer.Serialize(new AppStateModel { Mode = (ConnectionMode)42, TunStack = (TunStack)42 });

        using var document = JsonDocument.Parse(json);
        Assert.Equal("proxy", document.RootElement.GetProperty("mode").GetString());
        Assert.Equal("", document.RootElement.GetProperty("tunStack").GetString());
    }

    [Fact]
    public void SettingValueMaps_RoundtripEveryValue()
    {
        AssertRoundtrip(ConnectionPolicy.Modes, ["proxy", "tun", "tun_apps"]);
        AssertRoundtrip(DnsPolicy.Modes, ["doh", "system"]);
        AssertRoundtrip(DnsPolicy.Detours, ["direct", "proxy"]);
        AssertRoundtrip(RuleSetPolicyValidator.Actions, ["direct", "block"]);
        AssertRoundtrip(TunSettingsPolicy.Stacks, ["", "system", "mixed", "gvisor"]);
        AssertRoundtrip(SingBoxLogLevelPolicy.Levels, ["trace", "debug", "info", "warn", "error", "fatal", "panic"]);
        AssertRoundtrip(AppCloseBehaviors.Values, ["tray", "exit"]);
    }

    private static void AssertRoundtrip<T>(SettingValueMap<T> map, string[] expected) where T : struct, Enum
    {
        Assert.Equal(Enum.GetValues<T>().Length, map.Values.Count);
        Assert.Equal(expected, map.Values.Select(map.ToValue));
        foreach (var text in expected)
            Assert.Equal(text, map.ToValue(map.Parse(text)));
    }

    private static void WriteEncrypted(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, [.. "NV1"u8, .. protectedBytes]);
    }

    private static string ReadEncrypted(string path)
    {
        var data = File.ReadAllBytes(path);
        Assert.Equal("NV1", Encoding.ASCII.GetString(data, 0, 3));
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(data[3..], null, DataProtectionScope.CurrentUser));
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
