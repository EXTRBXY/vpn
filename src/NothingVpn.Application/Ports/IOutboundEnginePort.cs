using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Ports;

/// <summary>
/// Внешний движок, который сам соединяется с сервером профиля (ADR-0002); sing-box передаёт ему трафик через
/// локальный SOCKS5 <see cref="LocalSocksUpstream"/>. Конфиг движка лежит открытым текстом и удаляется в <see cref="Stop"/>.
/// </summary>
public interface IOutboundEnginePort
{
    OutboundEngine Engine { get; }
    event EventHandler? ProcessExited;
    bool IsRunning { get; }

    /// <summary>Пишет конфиг, проверяет его самим движком и запускает процесс. Порт может ещё не слушать.</summary>
    LocalSocksUpstream Start(VpnProfile profile, AppStateModel state);

    void Stop();
}

/// <summary>SOCKS5 на 127.0.0.1 с одноразовыми учётными данными; <see cref="ProcessPath"/> исключается из TUN.</summary>
public sealed record LocalSocksUpstream(int Port, string Username, string Password, string ProcessPath)
{
    public override string ToString() => $"{nameof(LocalSocksUpstream)} {{ Port = {Port} }}";
}
