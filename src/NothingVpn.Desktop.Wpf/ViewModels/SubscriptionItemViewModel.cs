using NothingVpn.Application.Models;

namespace NothingVpn.Desktop.Wpf;

public sealed class SubscriptionItemViewModel
{
    public SubscriptionItemViewModel(SubscriptionModel model) => Model = model;
    public SubscriptionModel Model { get; }
    public string Name => Model.Name;
    public string State => !Model.Enabled ? "Отключена" : !string.IsNullOrWhiteSpace(Model.LastError) ? "Ошибка обновления" : Model.LastSyncUtc is null ? "Не обновлялась" : "Обновлена";
    public string LastSync => Model.LastSyncUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string Traffic => $"↑ {FormatBytes(Model.UserInfo.Upload)}   ↓ {FormatBytes(Model.UserInfo.Download)}";
    public string Expires => Model.UserInfo.ExpireUtc is { } expires && expires.ToUnixTimeSeconds() > 0
        ? expires.ToLocalTime().ToString("dd.MM.yyyy")
        : "Без ограничения";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
