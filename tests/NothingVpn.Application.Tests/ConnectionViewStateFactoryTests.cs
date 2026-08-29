using NothingVpn.Application.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class ConnectionViewStateFactoryTests
{
    [Fact]
    public void Create_StoppedProxyWithProfile_CanStartAndShowsSummary()
    {
        var state = new AppStateModel
        {
            Mode = "proxy",
            LocalMixedPort = 1080,
            ProxyOverride = string.Empty
        };
        var profile = new VpnProfile { Id = "p1", Name = "Amsterdam" };

        var viewState = ConnectionViewStateFactory.Create(
            state,
            profile,
            isRunning: false,
            isAdministrator: false);

        Assert.True(viewState.CanStart);
        Assert.False(viewState.CanStop);
        Assert.True(viewState.CanEditConnection);
        Assert.Equal("Amsterdam", viewState.ProfileText);
        Assert.Equal("Прокси", viewState.ModeText);
        Assert.Equal("Nothing VPN (прокси)", viewState.WindowTitle);
    }

    [Fact]
    public void Create_RunningConnection_DisablesEditing()
    {
        var state = new AppStateModel { Mode = "tun_apps" };

        var viewState = ConnectionViewStateFactory.Create(
            state,
            new VpnProfile { Id = "p1", Name = "Profile" },
            isRunning: true,
            isAdministrator: true);

        Assert.False(viewState.CanStart);
        Assert.True(viewState.CanStop);
        Assert.False(viewState.CanEditConnection);
        Assert.False(viewState.CanEditTunApps);
        Assert.Equal("Администратор", viewState.AdministratorText);
    }

    [Fact]
    public void Create_StoppedTunApps_EnablesAppSelectionAndDescribesDnsHijack()
    {
        var state = new AppStateModel
        {
            Mode = "tun_apps",
            DnsMode = "doh",
            DnsDetour = "proxy"
        };

        var viewState = ConnectionViewStateFactory.Create(
            state,
            selectedProfile: null,
            isRunning: false,
            isAdministrator: true);

        Assert.True(viewState.CanEditTunApps);
        Assert.False(viewState.CanStart);
        Assert.Contains("hijack", viewState.DnsText, StringComparison.Ordinal);
        Assert.Equal("TUN (выбранные приложения)", viewState.ModeText);
    }
}
