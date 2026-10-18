namespace NothingVpn.Application.Models;

/// <summary>
/// Runtime-флаги подключения, которые лежат в state.json рядом с настройками (формат на диске не меняется).
/// Пишет их только <see cref="Services.VpnConnectionService"/>; сохранение настроек переносит их из свежей копии
/// состояния, поэтому устаревший снимок из UI не может их затереть.
/// </summary>
public sealed record ConnectionRuntimeState(bool ProxyWasEnabledByUs, ProxySettingsSnapshotModel? PreviousProxySettings)
{
    public static ConnectionRuntimeState From(AppStateModel state) =>
        new(state.ProxyWasEnabledByUs, CopySnapshot(state.PreviousProxySettings));

    public void ApplyTo(AppStateModel state)
    {
        state.ProxyWasEnabledByUs = ProxyWasEnabledByUs;
        state.PreviousProxySettings = CopySnapshot(PreviousProxySettings);
    }

    private static ProxySettingsSnapshotModel? CopySnapshot(ProxySettingsSnapshotModel? source) => source is null
        ? null
        : new ProxySettingsSnapshotModel
        {
            ProxyEnable = source.ProxyEnable,
            ProxyServer = source.ProxyServer,
            ProxyOverride = source.ProxyOverride
        };
}
