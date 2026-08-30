using NothingVpn.Application.Models;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

public sealed class AppCloseBehaviorTests
{
    [Theory]
    [InlineData(null, AppCloseBehavior.HideToTray)]
    [InlineData("", AppCloseBehavior.HideToTray)]
    [InlineData("unknown", AppCloseBehavior.HideToTray)]
    [InlineData("tray", AppCloseBehavior.HideToTray)]
    [InlineData("EXIT", AppCloseBehavior.Exit)]
    public void Normalize_returns_supported_value(string? value, string expected) =>
        Assert.Equal(expected, AppCloseBehavior.Normalize(value));

    [Theory]
    [InlineData(AppCloseBehavior.HideToTray)]
    [InlineData(AppCloseBehavior.Exit)]
    public void Legacy_mapping_preserves_close_behavior(string behavior)
    {
        var stored = LegacyModelMapper.ToLegacy(new AppStateModel { CloseBehavior = behavior });
        var restored = LegacyModelMapper.ToModel(stored);

        Assert.Equal(behavior, restored.CloseBehavior);
    }

    [Fact]
    public void Legacy_state_defaults_to_tray_behavior() =>
        Assert.Equal(AppCloseBehavior.HideToTray, new AppState().CloseBehavior);
}
