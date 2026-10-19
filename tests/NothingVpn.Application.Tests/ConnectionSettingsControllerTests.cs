using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ConnectionSettingsControllerTests
{
    [Fact]
    public void Save_ValidDraft_NormalizesAndPersistsAllSettings()
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel { Mode = ConnectionMode.Tun };
        var controller = new ConnectionSettingsController(settings);

        settings.State = state;
        controller.Save(state, CreateDraft());

        Assert.Equal("localhost;127.*", state.ProxyOverride);
        Assert.Equal("MyTun", state.TunInterfaceName);
        Assert.Equal("auto", state.TunAddressCidr);
        Assert.Equal(1500, state.TunMtu);
        Assert.Equal("doh", state.DnsMode.ToValue());
        Assert.Equal("/dns-query", state.DohPath);
        Assert.Equal("proxy", state.DnsDetour.ToValue());
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void Save_TunAppsMode_ForcesDnsDetourToDirect()
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel { Mode = ConnectionMode.TunApps };
        var controller = new ConnectionSettingsController(settings);

        settings.State = state;
        controller.Save(state, CreateDraft());

        Assert.Equal("direct", state.DnsDetour.ToValue());
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void Save_InvalidDns_DoesNotPartiallyChangeStateOrPersist()
    {
        var settings = new FakeSettingsService();
        var state = new AppStateModel
        {
            ProxyOverride = "original-proxy",
            TunInterfaceName = "OriginalTun",
            DohServer = "8.8.8.8"
        };
        var controller = new ConnectionSettingsController(settings);
        var draft = CreateDraft();
        draft.Dns.DohServer = " ";

        var error = Assert.Throws<InvalidOperationException>(() => controller.Save(state, draft));

        Assert.Equal("DoH IP не задан.", error.Message);
        Assert.Equal("original-proxy", state.ProxyOverride);
        Assert.Equal("OriginalTun", state.TunInterfaceName);
        Assert.Equal("8.8.8.8", state.DohServer);
        Assert.Equal(0, settings.SaveCalls);
    }

    private static ConnectionSettingsDraft CreateDraft() => new(
        new ProxyConnectionSettings { ProxyOverride = "  localhost;127.*  " },
        new TunSettings
        {
            InterfaceName = " MyTun ",
            AddressCidr = "172.19.0.1/30",
            Mtu = 9000,
            Stack = TunSettingsPolicy.Stacks.Parse("SYSTEM"),
            AutoRoute = true,
            StrictRoute = false
        },
        new DnsSettings
        {
            Mode = DnsPolicy.Modes.Parse(" DOH "),
            DohServer = " 1.1.1.1 ",
            DohPath = " ",
            DohSni = " cloudflare-dns.com ",
            Detour = DnsDetour.Proxy
        });

    private sealed class FakeSettingsService : ISettingsService
    {
        public event EventHandler<AppStateModel>? StateChanged
        {
            add { }
            remove { }
        }

        public int SaveCalls { get; private set; }
        public AppStateModel State { get; set; } = new();
        public AppStateModel GetState() => State;
        public void UpdateState(Action<AppStateModel> update) { update(State); SaveCalls++; }
        public void UpdateMode(ConnectionMode mode) => throw new NotSupportedException();
        public void UpdateDns(DnsMode mode, string dohServer, string dohPath, string dohSni, DnsDetour detour) => throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }
}
