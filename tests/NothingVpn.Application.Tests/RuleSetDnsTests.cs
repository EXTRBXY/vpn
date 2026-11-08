using System.IO.Compression;
using System.Text.Json.Nodes;
using NothingVpn.Application.Models;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.RuleSets;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Infrastructure.ServiceHost;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

public sealed class RuleSetDnsTests : IDisposable
{
    // Compiled by sing-box 1.13.21. Fixtures stay executable-free in CI.
    // domain_suffix: direct.example; ip_cidr: 203.0.113.0/24, 2001:db8::/32.
    private const string DomainList = "U1JTAnjaYmRgYmBkAIMGCCNr1apV/Kk5BbmJFal6JcmpRZkpXP8ZAAEAAP//flwJmw==";
    private const string IpList = "U1JTAnjaYmRgY2SAACaW0wyFDCDiv4ACI+8OBiQAFviPDBgAAQAA//8l5xJp";
    private const string MixedList = "U1JTAnjaYmJgYmBkAIMGCCNr1apV/Kk5BbmJFal6JcmpRZkpXP8ZGNigqhgYWU4zFDKAiP//GQABAAD//3S8DiI=";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<string, bool> ContentCases => new()
    {
        { DomainList, false },
        { "U1JTAXjaYmRgYmBkAAFGBzCDcdWqVasEUnMKchMrUvVKklOLMlP0eP8zAAIAAP//hjkJzw==", false },
        { IpList, true },
        { MixedList, true },
        // Nested logical OR/AND with IP rules.
        { "U1JTAnjaYmRkZGJgYmBkAIMGCCNr1apV/Kk5BbmJFal6JcmpRZkpXP8ZGBmYGNigChkYWU4zFDKAiP//GRg4GRl3/2dgYAAEAAD//xoYD+0=", true },
        // Inverted logical domain rules: keyword + regex.
        { "U1JTAnjaYmRkYGJgYRSMS8ksSk0uidFLrUjMLchJVfnPwMDMyAYR/c/ACAgAAP//wl8LEg==", false },
        // AdGuard ||direct.example^, converted using rule-set convert.
        { "U1JTAnjaYmQQYGBkAIMGCCNr1apV/Kk5BbmJFal6JcmpRZkpXP8ZAAEAAP//gHAJqQ==", false },
        // All remaining v3 fields, including query types, ports, processes, networks, Wi-Fi.
        { "U1JTA3jaVMhBCoJAGAXg/6WkZhlWq9at5S9qM6uoa7Qx/ReC0jAONGeaK3SLLlSQQfR4i+89EIEABLbSIwJ9chyw9t5n0uqudFLYSkxTBxjXjZHKhojFlZ1uZRMhfMZItsysdsycAI8Joj2zOjCnSK/mdu/FFOJkiuysLqffkSP6rhlCK71dgJarbPAcObP674veAQAA//+GpikK", false },
        { "U1JTAnjaYmRgYmBkAAMHCENq1apVfKk5BbmJFal6JcmpRZkprFAVDIwsB1YwMLAcWPH//38GQAAAAP//JpsN3Q==", true },
        // v4 network_interface_address.
        { "U1JTBHjaYmRgYmBkAAMHCENq1apVfKk5BbmJFal6JcmpRZkpoowMjCwHGJgYJP4zAAIAAP//vtcJ9Q==", true }
    };

    [Theory]
    [MemberData(nameof(ContentCases))]
    public void Inspect_CompiledContent_DetectsIpConditions(string content, bool expected)
    {
        var file = WriteList("arbitrary.srs", content);
        Assert.Equal(expected, SrsRuleSetInspector.HasIpConditions(file));
    }

    public static TheoryData<ConnectionMode, bool, string, bool> ConfigCases
    {
        get
        {
            var data = new TheoryData<ConnectionMode, bool, string, bool>();
            foreach (var mode in new[] { ConnectionMode.Proxy, ConnectionMode.Tun, ConnectionMode.TunApps })
            foreach (var xray in new[] { false, true })
            foreach (var (content, isIp) in new[] { (DomainList, false), (IpList, true), (MixedList, true) })
                data.Add(mode, xray, content, isIp);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ConfigCases))]
    public void WriteConfig_UsesContentForSplitDns_AndPreservesRouting(
        ConnectionMode mode, bool xray, string content, bool isIp)
    {
        // Deliberately misleading names: IP list looks like geosite, domain list like geoip.
        var tag = isIp ? "geosite-custom" : "geoip-custom";
        WriteList(tag + ".srs", content);
        var state = new AppStateModel
        {
            Mode = mode,
            DnsMode = DnsMode.Doh,
            TunAppProcessPaths = [@"C:\Apps\browser.exe"],
            DomainExceptions = [new() { Domain = "manual.example", Enabled = true, IncludeSubdomains = true }],
            UserRuleSets = [new() { Tag = tag, FileName = tag + ".srs", Enabled = true, Action = RuleSetAction.Direct }]
        };
        var json = WriteConfig(state, xray);
        var dnsRules = json["dns"]!["rules"]!.AsArray();
        Assert.Equal(!isIp, dnsRules.Any(r => r?["rule_set"]?[0]?.GetValue<string>() == tag));
        Assert.Contains(dnsRules, r => r?["domain_suffix"]?[0]?.GetValue<string>() == "manual.example"
            && r?["server"]?.GetValue<string>() == "bootstrap-local");
        Assert.Equal("doh", json["dns"]!["final"]!.GetValue<string>());
        Assert.Contains(json["route"]!["rules"]!.AsArray(), r =>
            r?["rule_set"]?[0]?.GetValue<string>() == tag && r?["outbound"]?.GetValue<string>() == "direct");
        Assert.Contains(json["route"]!["rule_set"]!.AsArray(), r => r?["tag"]?.GetValue<string>() == tag);
    }

    [Fact]
    public void WriteConfig_ServiceRenamesFiles_ContentStillDeterminesSplitDns()
    {
        var store = new ServiceRuleSetStore(_directory);
        var files = store.Prepare(
        [
            new() { Tag = "user-ip", FileName = "renamed.srs", Content = Convert.FromBase64String(IpList) },
            new() { Tag = "geoip-domain", FileName = "anything.srs", Content = Convert.FromBase64String(DomainList) }
        ]);
        Assert.All(files, file => Assert.Matches("^[0-9]{2}-[a-f0-9]{16}\\.srs$", file));
        var json = WriteConfig(new AppStateModel
        {
            DnsMode = DnsMode.Doh,
            UserRuleSets =
            [
                new() { Tag = "user-ip", FileName = files[0], Enabled = true, Action = RuleSetAction.Direct },
                new() { Tag = "geoip-domain", FileName = files[1], Enabled = true, Action = RuleSetAction.Direct }
            ]
        }, false);
        var rules = json["dns"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r?["rule_set"]?[0]?.GetValue<string>() == "user-ip");
        Assert.Contains(rules, r => r?["rule_set"]?[0]?.GetValue<string>() == "geoip-domain");
    }

    [Fact]
    public void WriteConfig_SystemDnsOrDisabledOrBlock_DoesNotInspectUnusedContent()
    {
        foreach (var dns in new[] { DnsMode.System, DnsMode.Doh })
        {
            var state = new AppStateModel
            {
                DnsMode = dns,
                UserRuleSets =
                [
                    new() { Tag = "disabled", FileName = "missing.srs", Enabled = false },
                    new() { Tag = "block", FileName = "missing.srs", Enabled = true, Action = RuleSetAction.Block }
                ]
            };
            if (dns == DnsMode.System)
                state.UserRuleSets.Add(new() { Tag = "direct", FileName = "missing.srs", Enabled = true });
            Assert.NotNull(WriteConfig(state, false));
        }
    }

    [Fact]
    public void WriteConfig_InvalidDirectList_FailsInsteadOfGuessingDnsBehavior()
    {
        WriteList("broken.srs", "AA==");
        var error = Assert.Throws<InvalidOperationException>(() => WriteConfig(new AppStateModel
        {
            DnsMode = DnsMode.Doh,
            UserRuleSets = [new() { Tag = "broken", FileName = "broken.srs", Enabled = true, Action = RuleSetAction.Direct }]
        }, false));
        Assert.Contains("broken.srs", error.Message);
        Assert.Empty(Directory.GetFiles(_directory, "*.json"));
    }

    [Theory]
    [InlineData("AA==")]
    [InlineData("U1JTBQ==")]
    [InlineData("U1JTAg==")]
    public void Inspect_InvalidContent_FailsClearly(string content)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SrsRuleSetInspector.HasIpConditions(WriteList("broken.srs", content)));
        Assert.Contains("broken.srs", error.Message);
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 99 })] // unknown condition
    [InlineData(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 })] // oversized varint
    public void Inspect_MalformedStructure_FailsClearly(byte[] payload)
    {
        using var output = new MemoryStream();
        output.Write([83, 82, 83, 3]);
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(payload);
        Assert.Throws<InvalidOperationException>(() => SrsRuleSetInspector.HasIpConditions(
            WriteList("malformed.srs", Convert.ToBase64String(output.ToArray()))));
    }

    private string WriteList(string name, string content)
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, name);
        File.WriteAllBytes(file, Convert.FromBase64String(content));
        return file;
    }

    private JsonNode WriteConfig(AppStateModel state, bool xray)
    {
        Directory.CreateDirectory(_directory);
        var paths = new AppPaths(_directory, _directory, _directory, _directory, "profiles.json", "subscriptions.json", "state.json");
        var log = new InMemoryLogStore();
        using var runner = new SingBoxRunner("unused.exe", log);
        var port = new SingBoxPort(new LegacyRuntimeContext(paths, log, runner));
        var profile = new VpnProfile { Id = "test", Host = "node.example", Port = 443, Security = "tls", Sni = "node.example" };
        var upstream = xray ? new LocalSocksUpstream(12345, "test", "test", @"C:\NothingVpn\xray.exe") : null;
        return JsonNode.Parse(File.ReadAllText(port.WriteConfig(profile, state, upstream)))!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
