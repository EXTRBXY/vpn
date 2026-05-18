using NothingVpn.Application.Models;

namespace NothingVpn.Tray;

internal static class SubscriptionDisplayHelper
{
    public static string FormatTraffic(SubscriptionUserInfoModel info)
    {
        return $"↑ {FormatBytes(info.Upload)} / ↓ {FormatBytes(info.Download)} / {FormatBytes(info.Total)}";
    }

    public static string FormatExpire(DateTimeOffset? expireUtc)
    {
        if (expireUtc is null || expireUtc.Value.ToUnixTimeSeconds() <= 0)
            return "—";
        return expireUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    public static string FormatLastSync(SubscriptionModel subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.LastError))
            return "Ошибка: " + subscription.LastError;

        if (subscription.LastSyncUtc is null)
            return "Не синхронизировалась";

        return subscription.LastSyncUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
