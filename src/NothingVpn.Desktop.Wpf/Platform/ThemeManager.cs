using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NothingVpn.Desktop.Wpf;

internal static class ThemeManager
{
    public static void ApplySystemTheme()
    {
        var dark = IsDarkTheme();
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries.FirstOrDefault(x =>
                x.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
            ?? System.Windows.Application.Current.Resources;

        Set(resources, "WindowBrush", dark ? "#111318" : "#F4F6FA");
        Set(resources, "SurfaceBrush", dark ? "#1B1E24" : "#FFFFFF");
        Set(resources, "TextBrush", dark ? "#F2F4F7" : "#18202A");
        Set(resources, "MutedBrush", dark ? "#D0D5DD" : "#344054");
        Set(resources, "BorderBrush", dark ? "#3B424E" : "#D7DCE5");
        Set(resources, "SubtleBrush", dark ? "#242932" : "#EEF1F5");
        Set(resources, "HoverBrush", dark ? "#2B3445" : "#F5F7FA");
        Set(resources, "SelectedBrush", dark ? "#263B68" : "#EEF3FF");
        Set(resources, "DangerBrush", dark ? "#FDA29B" : "#B42318");
        Set(resources, "DangerSurfaceBrush", dark ? "#4A2424" : "#FDECEA");
        Set(resources, "WarningBrush", dark ? "#FEC84B" : "#7A4D00");
        Set(resources, "WarningSurfaceBrush", dark ? "#433515" : "#FFF5D9");
        Set(resources, "ScrollTrackBrush", dark ? "#242932" : "#EEF1F5");
        Set(resources, "ScrollThumbBrush", dark ? "#596273" : "#98A2B3");
        Set(resources, "ScrollThumbHoverBrush", dark ? "#7B8493" : "#667085");
    }

    public static bool IsDarkTheme()
    {
        if (SystemParameters.HighContrast) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }
}
