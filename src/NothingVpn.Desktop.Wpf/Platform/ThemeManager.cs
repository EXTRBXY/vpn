using System.Windows.Media;
using Microsoft.Win32;

namespace NothingVpn.Desktop.Wpf;
internal static class ThemeManager
{
    public static void ApplySystemTheme()
    {
        var dark=IsDark(); var r=System.Windows.Application.Current.Resources;
        Set(r,"WindowColor",dark?"#111318":"#F4F6FA"); Set(r,"SurfaceColor",dark?"#1B1E24":"#FFFFFF");
        Set(r,"TextColor",dark?"#F2F4F7":"#18202A"); Set(r,"MutedColor",dark?"#D0D5DD":"#344054");
        Set(r,"BorderColor",dark?"#3B424E":"#D7DCE5"); Set(r,"SubtleColor",dark?"#242932":"#EEF1F5");
        Set(r,"HoverColor",dark?"#2B3445":"#E5EBF8"); Set(r,"SelectedColor",dark?"#263B68":"#DCE7FF");
        Set(r,"DangerColor",dark?"#FDA29B":"#B42318"); Set(r,"DangerSurfaceColor",dark?"#4A2424":"#FDECEA");
        Set(r,"WarningColor",dark?"#FEC84B":"#7A4D00"); Set(r,"WarningSurfaceColor",dark?"#433515":"#FFF5D9");
    }
    private static bool IsDark()
    {
        if(System.Windows.SystemParameters.HighContrast)return false;
        try{using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return k?.GetValue("AppsUseLightTheme") is int value&&value==0;}catch{return false;}
    }
    private static void Set(System.Windows.ResourceDictionary r,string key,string hex)=>r[key]=(System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
}
