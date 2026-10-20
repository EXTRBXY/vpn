namespace NothingVpn.Application.Ports;

/// <summary>Источники событий самого клиента в <see cref="ILogPort"/> (у sing-box источник — его подсистема).</summary>
public static class AppLogSources
{
    public const string App = "app";
    public const string Connection = "app/connection";
    public const string Subscriptions = "app/subscriptions";
    public const string Update = "app/update";
    public const string Storage = "app/storage";
    public const string Ui = "app/ui";
}
