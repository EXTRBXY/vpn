using System.Text.Json;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

public sealed class DomainExceptionTests
{
    [Theory]
    [InlineData(" HitMos.FM. ", "hitmos.fm")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    public void Normalize_CanonicalizesDomains(string input, string expected) =>
        Assert.Equal(expected, DomainExceptionPolicy.NormalizeDomain(input));

    [Theory]
    [InlineData("")]
    [InlineData("https://hitmos.fm")]
    [InlineData("hitmos.fm/music")]
    [InlineData("hitmos.fm:443")]
    [InlineData("*.hitmos.fm")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("a..fm")]
    [InlineData("hitmos.fm..")]
    [InlineData("-hitmos.fm")]
    [InlineData("hit_mos.fm")]
    public void Normalize_RejectsNonDomains(string input) =>
        Assert.Throws<ArgumentException>(() => DomainExceptionPolicy.NormalizeDomain(input));

    [Fact]
    public void Validate_RejectsDuplicatesAndOversizedLists()
    {
        Assert.Throws<ArgumentException>(() => DomainExceptionPolicy.Validate(["hitmos.fm", "HITMOS.FM."]));
        Assert.Throws<ArgumentException>(() => DomainExceptionPolicy.Validate(
            Enumerable.Range(0, DomainExceptionPolicy.MaxEntries + 1).Select(i => $"site{i}.fm")));
    }

    [Theory]
    [InlineData(ConnectionMode.Proxy, DnsMode.Doh)]
    [InlineData(ConnectionMode.Tun, DnsMode.Doh)]
    [InlineData(ConnectionMode.TunApps, DnsMode.Doh)]
    [InlineData(ConnectionMode.Proxy, DnsMode.System)]
    [InlineData(ConnectionMode.Tun, DnsMode.System)]
    [InlineData(ConnectionMode.TunApps, DnsMode.System)]
    public void Config_ExceptionsPrecedeListsAndFallbacks_WithMatchingDns(ConnectionMode mode, DnsMode dns)
    {
        var state = new AppStateModel
        {
            Mode = mode, DnsMode = dns, DnsDetour = DnsDetour.Proxy,
            TunAppProcessPaths = [@"C:\Apps\browser.exe"],
            DomainExceptions = [new() { Domain = "hitmos.fm" }, new() { Domain = "exact.fm", IncludeSubdomains = false },
                new() { Domain = "disabled.fm", Enabled = false }],
            UserRuleSets = [new() { Tag = "block-sites", FileName = "sites.srs", Action = RuleSetAction.Block }]
        };
        AppStateNormalizer.Normalize(state, new PathPolicyPort());
        var profile = new VpnProfile { Id = "p1", Host = "server.example", Uuid = Guid.NewGuid().ToString(), Security = "tls", Type = "tcp" };
        var request = JsonSerializer.Deserialize<EngineConnectRequest>(JsonSerializer.Serialize(EngineRequestMapper.CreateRequest(profile, state)))!;
        Assert.Equal(2, request.DomainExceptions.Count);
        var paths = new AppPaths("root", "configs", "rulesets", "logs", "profiles", "subscriptions", "state");
        var config = SingBoxConfigGenerator.Build(paths, EngineRequestMapper.ToProfile(request), EngineRequestMapper.ToState(request));
        var rules = config.Route!.Rules!;
        var suffixIndex = rules.FindIndex(x => x.DomainSuffix?.Contains("hitmos.fm") == true);
        var exactIndex = rules.FindIndex(x => x.Domain?.Contains("exact.fm") == true);
        var listIndex = rules.FindIndex(x => x.RuleSet?.Contains("block-sites") == true);
        Assert.True(suffixIndex >= 0 && exactIndex > suffixIndex && listIndex > exactIndex);
        Assert.Equal("direct", rules[suffixIndex].Outbound);
        Assert.Null(rules[suffixIndex].Domain);
        Assert.Null(rules[exactIndex].DomainSuffix);
        Assert.DoesNotContain(rules, x => x.DomainSuffix?.Contains("disabled.fm") == true);
        Assert.DoesNotContain(rules.Take(listIndex), x => x.Protocol == "quic" || x.IpVersion == 6 || x.ProcessName is not null);
        var configDir = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        try
        {
            var file = SingBoxConfigGenerator.WriteConfig(paths with { ConfigsDir = configDir }, profile, state);
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Contains(json.RootElement.GetProperty("route").GetProperty("rules").EnumerateArray(),
                x => x.TryGetProperty("domain_suffix", out var domains) && domains[0].GetString() == "hitmos.fm");
        }
        finally { Directory.Delete(configDir, recursive: true); }
        if (dns == DnsMode.Doh)
        {
            var suffix = Assert.Single(config.Dns!.Rules!, x => x.DomainSuffix?.Contains("hitmos.fm") == true);
            Assert.Equal(TunBootstrapPolicy.BootstrapLocalDnsTag, suffix.Server);
            Assert.Contains(config.Dns.Servers, x => x.Type == "local" && x.Tag == suffix.Server);
            Assert.Contains(config.Dns.Rules!, x => x.Domain?.Contains("exact.fm") == true && x.Server == suffix.Server);
        }
        else Assert.Equal(TunBootstrapPolicy.BootstrapLocalDnsTag, config.Dns!.Final);
        Assert.Equal(mode != ConnectionMode.Proxy, config.Dns!.ReverseMapping == true);
    }

    [Fact]
    public void Settings_AddToggleRemoveAndSave_PreservesDraftIsolation()
    {
        var h = new SettingsViewModelTests.Harness();
        var vm = h.Create();
        vm.Exceptions.NewDomain = " HITMOS.FM ";
        vm.Exceptions.AddCommand.Execute(null);
        Assert.Empty(h.Store.State.DomainExceptions);
        Assert.Equal("hitmos.fm", Assert.Single(vm.Exceptions.Entries).Domain);
        vm.Exceptions.NewDomain = "hitmos.fm";
        vm.Exceptions.AddCommand.Execute(null);
        Assert.Single(vm.Exceptions.Entries);
        Assert.Contains("уже", vm.Message);
        vm.Exceptions.Entries[0].Enabled = false;
        vm.Exceptions.Entries[0].IncludeSubdomains = false;
        vm.SaveCommand.Execute(null);
        var reopened = h.Create();
        var entry = Assert.Single(reopened.Exceptions.Entries);
        Assert.False(entry.Enabled);
        Assert.False(entry.IncludeSubdomains);
        reopened.Exceptions.SelectedEntry = entry;
        reopened.Exceptions.RemoveCommand.Execute(null);
        Assert.Single(h.Store.State.DomainExceptions);
        reopened.SaveCommand.Execute(null);
        Assert.Empty(h.Create().Exceptions.Entries);
    }

    [Fact]
    public void InvalidException_DoesNotSaveOtherSettings()
    {
        var h = new SettingsViewModelTests.Harness();
        var vm = h.Create();
        vm.Tun.MtuText = "1400";
        vm.Exceptions.Entries.Add(new() { Domain = "https://hitmos.fm" });
        vm.SaveCommand.Execute(null);
        Assert.Equal(0, h.Store.SaveCalls);
        Assert.Empty(h.Store.State.DomainExceptions);
        Assert.NotNull(vm.Message);
    }

    [Fact]
    public void DisabledExceptions_DoNotCreateRulesOrDns()
    {
        var state = new AppStateModel { DnsMode = DnsMode.System,
            DomainExceptions = [new() { Domain = "hitmos.fm", Enabled = false }] };
        var paths = new AppPaths("root", "configs", "rulesets", "logs", "profiles", "subscriptions", "state");
        var config = SingBoxConfigGenerator.Build(paths, new() { Host = "node.example", Security = "tls", Type = "tcp" }, state);
        Assert.Null(config.Dns);
        Assert.DoesNotContain(config.Route.Rules!, x => x.DomainSuffix is not null);
    }

    [Fact]
    public void Tun_ExceptionPrecedesKnownSecureDnsProxyRule()
    {
        var state = new AppStateModel
        {
            Mode = ConnectionMode.Tun,
            DnsMode = DnsMode.Doh,
            DnsDetour = DnsDetour.Proxy,
            DohServer = "1.1.1.1",
            DohSni = "cloudflare-dns.com",
            DomainExceptions = [new() { Domain = "cloudflare-dns.com", IncludeSubdomains = false }]
        };
        AppStateNormalizer.Normalize(state, new PathPolicyPort());
        var paths = new AppPaths("root", "configs", "rulesets", "logs", "profiles", "subscriptions", "state");
        var config = SingBoxConfigGenerator.Build(paths,
            new() { Host = "node.example", Security = "tls", Type = "tcp" }, state);
        var rules = config.Route!.Rules!;
        var exceptionIndex = rules.FindIndex(x =>
            x.Outbound == "direct" && x.Domain?.Contains("cloudflare-dns.com") == true);
        var secureDnsIndex = rules.FindIndex(x =>
            x.Outbound == "proxy" && x.Domain?.Contains("cloudflare-dns.com") == true);
        Assert.True(exceptionIndex >= 0, "ожидалось route-исключение на cloudflare-dns.com → direct");
        Assert.True(secureDnsIndex > exceptionIndex,
            "KnownSecureDns → proxy должен идти после пользовательского исключения");
    }

    [Fact]
    public void Tun_RejectsOwnPrefixesBeforePrivateDirect()
    {
        var state = new AppStateModel { Mode = ConnectionMode.Tun, DnsMode = DnsMode.System };
        AppStateNormalizer.Normalize(state, new PathPolicyPort());
        var profile = new VpnProfile { Id = "abcdef12-3456-7890-abcd-ef1234567890", Host = "1.2.3.4", Security = "tls", Type = "tcp" };
        var paths = new AppPaths("root", "configs", "rulesets", "logs", "profiles", "subscriptions", "state");
        var config = SingBoxConfigGenerator.Build(paths, profile, state);
        var rules = config.Route!.Rules!;
        var ipv4 = TunInterfacePolicy.Ipv4Address(state.TunAddressCidr, profile.Id);
        var ipv6 = TunInterfacePolicy.Ipv6Address(profile.Id);
        var rejectIndex = rules.FindIndex(x =>
            x.Action == "reject" && x.Method == "drop" &&
            x.IpCidr is not null && x.IpCidr.Contains(ipv4) && x.IpCidr.Contains(ipv6));
        var privateIndex = rules.FindIndex(x => x.IpIsPrivate == true && x.Outbound == "direct");
        Assert.True(rejectIndex >= 0, "ожидался reject/drop собственных префиксов TUN");
        Assert.True(privateIndex > rejectIndex, "reject TUN self должен быть до ip_is_private → direct");
        var hijackIndex = rules.FindIndex(x => x.Action == "hijack-dns");
        Assert.True(hijackIndex >= 0 && hijackIndex < rejectIndex, "hijack-dns должен быть до reject self");
    }

    [Fact]
    public void OldState_DefaultsToNoExceptions() =>
        Assert.Empty(JsonSerializer.Deserialize<AppStateModel>("{}")!.DomainExceptions);
}
