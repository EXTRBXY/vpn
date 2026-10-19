using NothingVpn.Domain.Models;
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
            Mode = ConnectionMode.Proxy,
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
        var state = new AppStateModel { Mode = ConnectionMode.TunApps };

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
            Mode = ConnectionMode.TunApps,
            DnsMode = DnsMode.Doh,
            DnsDetour = DnsDetour.Proxy
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

    [Theory]
    [InlineData(DnsDetour.Proxy, "Через VPN")]
    [InlineData(DnsDetour.Direct, "Напрямую")]
    public void SettingLabels_Detour(DnsDetour detour, string expected) =>
        Assert.Equal(expected, SettingLabels.Detour(detour));

    [Theory]
    [InlineData(TunStack.Default, "Системный")]
    [InlineData(TunStack.System, "Системный")]
    [InlineData(TunStack.Mixed, "Смешанный")]
    [InlineData(TunStack.Gvisor, "gVisor")]
    public void SettingLabels_Stack(TunStack stack, string expected) =>
        Assert.Equal(expected, SettingLabels.Stack(stack));
}
