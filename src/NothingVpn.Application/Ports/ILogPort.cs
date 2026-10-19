using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Ports;

/// <summary>
/// Журнал приложения в памяти: вывод sing-box и события клиента. Реализация маскирует секреты при записи,
/// поэтому вызывающему коду не нужно чистить сообщения самому.
/// </summary>
public interface ILogPort
{
    /// <summary>Меняется при каждой записи и очистке; по нему UI понимает, что снимок устарел.</summary>
    int Version { get; }

    string SnapshotText(LogLevel minLevel);
    string? TryGetLatestMessage(LogLevel minLevel);
    void Append(LogLevel level, string source, string message);
    void Clear();
}
