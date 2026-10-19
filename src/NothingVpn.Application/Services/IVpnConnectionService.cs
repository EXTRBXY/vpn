using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IVpnConnectionService
{
    event EventHandler<bool>? ConnectionStateChanged;
    Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Дешёвый статус без обращения к диску: процесс — из <see cref="Engine.IVpnEngine.GetStatus"/>,
    /// режим и профиль — текущей сессии этого процесса (без сессии — значения по умолчанию).
    /// </summary>
    VpnConnectionStatus GetStatus();

    void RecoverStaleRuntimeState();
}
