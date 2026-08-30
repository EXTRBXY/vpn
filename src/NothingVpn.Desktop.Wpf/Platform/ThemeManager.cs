using System.Windows.Media;
using Microsoft.Win32;

namespace NothingVpn.Desktop.Wpf;
internal static class ThemeManager
{
    public static void ApplySystemTheme()
    {
        var dark=IsDark(); var r=System.Windows.Application.Current.Resources;
        Set(r,"WindowBrush",dark?"#111318":"#F4F6FA"); Set(r,"SurfaceBrush",dark?"#1B1E24":"#FFFFFF");
        Set(r,"TextBrush",dark?"#F2F4F7":"#18202A"); Set(r,"MutedBrush",dark?"#D0D5DD":"#344054");
        Set(r,"BorderBrush",dark?"#3B424E":"#D7DCE5"); Set(r,"SubtleBrush",dark?"#242932":"#EEF1F5");
        Set(r,"HoverBrush",dark?"#2B3445":"#E5EBF8"); Set(r,"SelectedBrush",dark?"#263B68":"#DCE7FF");
        Set(r,"DangerBrush",dark?"#FDA29B":"#B42318"); Set(r,"DangerSurfaceBrush",dark?"#4A2424":"#FDECEA");
        Set(r,"WarningBrush",dark?"#FEC84B":"#7A4D00"); Set(r,"WarningSurfaceBrush",dark?"#433515":"#FFF5D9");
    }
    private static bool IsDark()
    {
        if(System.Windows.SystemParameters.HighContrast)return false;
        try{using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return k?.GetValue("AppsUseLightTheme") is int value&&value==0;}catch{return false;}
    }
    private static void Set(System.Windows.ResourceDictionary r,string key,string hex)=>r[key]=new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
