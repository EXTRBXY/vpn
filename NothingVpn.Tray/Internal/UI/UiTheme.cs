namespace NothingVpn.Tray.Internal.UI;

internal static class UiTheme
{
    public static Color Accent => Color.FromArgb(32, 122, 230);
    public static Color Surface => SystemColors.Window;
    public static Color SurfaceAlt => Color.FromArgb(246, 248, 251);
    public static Color TextPrimary => SystemColors.ControlText;
    public static Color TextMuted => SystemColors.GrayText;
    public static Color Border => Color.FromArgb(218, 223, 231);

    public static bool IsHighContrast => SystemInformation.HighContrast;
}
