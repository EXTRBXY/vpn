using System.Windows.Media;
using Microsoft.Win32;

namespace NothingVpn.Desktop.Wpf;
internal static class ThemeManager
{
    public static void ApplySystemTheme()
    {
        var dark=IsDark(); var r=System.Windows.Application.Current.Resources;
        Set(r,"WindowBrush",dark?"#111318":"#F4F6FA"); Set(r,"SurfaceBrush",dark?"#1B1E24":"#FFFFFF");
        Set(r,"TextBrush",dark?"#F2F4F7":"#18202A"); Set(r,"MutedBrush",dark?"#AAB2C0":"#667085");
        Set(r,"BorderBrush",dark?"#303641":"#E3E7EE");
    }
    private static bool IsDark()
    {
        if(System.Windows.SystemParameters.HighContrast)return false;
        try{using var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return k?.GetValue("AppsUseLightTheme") is int value&&value==0;}catch{return false;}
    }
    private static void Set(System.Windows.ResourceDictionary r,string key,string hex)=>r[key]=new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
