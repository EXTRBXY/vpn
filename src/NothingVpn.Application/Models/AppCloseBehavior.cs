using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Models;

/// <summary>Поведение при закрытии окна. В state.json: tray | exit.</summary>
public enum AppCloseBehavior
{
    HideToTray,
    Exit
}

public static class AppCloseBehaviors
{
    public const AppCloseBehavior Default = AppCloseBehavior.HideToTray;

    public static readonly SettingValueMap<AppCloseBehavior> Values = new(
        Default,
        new Dictionary<AppCloseBehavior, string>
        {
            [AppCloseBehavior.HideToTray] = "tray",
            [AppCloseBehavior.Exit] = "exit"
        });

    public static string ToValue(this AppCloseBehavior value) => Values.ToValue(value);
}
