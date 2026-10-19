using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

public sealed class AppCloseBehaviorTests
{
    [Theory]
    [InlineData(null, AppCloseBehavior.HideToTray)]
    [InlineData("", AppCloseBehavior.HideToTray)]
    [InlineData("unknown", AppCloseBehavior.HideToTray)]
    [InlineData("tray", AppCloseBehavior.HideToTray)]
    [InlineData("EXIT", AppCloseBehavior.Exit)]
    public void Parse_returns_supported_value(string? value, AppCloseBehavior expected) =>
        Assert.Equal(expected, AppCloseBehaviors.Values.Parse(value));

    [Theory]
    [InlineData(AppCloseBehavior.HideToTray)]
    [InlineData(AppCloseBehavior.Exit)]
    public void Storage_roundtrip_preserves_close_behavior(AppCloseBehavior behavior)
    {
        var root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStorePort(new FakeAppPathsPort(root));
            store.Save(new AppStateModel { CloseBehavior = behavior });

            Assert.Equal(behavior, store.Load().CloseBehavior);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void State_defaults_to_tray_behavior() =>
        Assert.Equal(AppCloseBehavior.HideToTray, new AppStateModel().CloseBehavior);

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
