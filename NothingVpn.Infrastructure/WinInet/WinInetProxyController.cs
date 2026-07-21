using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NothingVpn.Infrastructure.WinInet;

internal sealed class WinInetProxyController
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public WinInetProxySettingsSnapshot ReadCurrent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
        var enable = key?.GetValue("ProxyEnable") as int? ?? 0;
        var server = key?.GetValue("ProxyServer") as string;
        var bypass = key?.GetValue("ProxyOverride") as string;

        return new WinInetProxySettingsSnapshot
        {
            ProxyEnable = enable != 0,
            ProxyServer = server,
            ProxyOverride = bypass
        };
    }

    public void Enable(string proxyServer, string proxyOverride)
    {
        if (string.IsNullOrWhiteSpace(proxyServer)) throw new ArgumentException("proxyServer is required.");

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("Cannot open WinINET Internet Settings registry key.");

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
        key.SetValue("ProxyOverride", proxyOverride ?? "", RegistryValueKind.String);

        NotifyWinInetSettingsChanged();
    }

    public void Restore(WinInetProxySettingsSnapshot? previous)
    {
        // If we don't have a snapshot, safest behavior is to disable proxy.
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("Cannot open WinINET Internet Settings registry key.");

        if (previous is null)
        {
            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            NotifyWinInetSettingsChanged();
            return;
        }

        key.SetValue("ProxyEnable", previous.ProxyEnable ? 1 : 0, RegistryValueKind.DWord);

        if (previous.ProxyServer is null) key.DeleteValue("ProxyServer", throwOnMissingValue: false);
        else key.SetValue("ProxyServer", previous.ProxyServer, RegistryValueKind.String);

        if (previous.ProxyOverride is null) key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
        else key.SetValue("ProxyOverride", previous.ProxyOverride, RegistryValueKind.String);

        NotifyWinInetSettingsChanged();
    }

    private static void NotifyWinInetSettingsChanged()
    {
        // INTERNET_OPTION_SETTINGS_CHANGED = 39, INTERNET_OPTION_REFRESH = 37
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}

internal sealed class WinInetProxySettingsSnapshot
{
    public bool ProxyEnable { get; set; }
    public string? ProxyServer { get; set; }
    public string? ProxyOverride { get; set; }
}

