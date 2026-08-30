namespace NothingVpn.Application.Models;

public static class AppCloseBehavior
{
    public const string HideToTray = "tray";
    public const string Exit = "exit";

    public static string Normalize(string? value) =>
        string.Equals(value, Exit, StringComparison.OrdinalIgnoreCase) ? Exit : HideToTray;
}
