using System.Text.Json.Serialization;
using NothingVpn.Application.Serialization;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Engine;

/// <summary>
/// Состояние ядра — ответ <c>GetStatus</c> и событие изменения состояния канала службы. Режим и профиль заданы,
/// пока ядро работает; по ним перезапущенный интерфейс узнаёт текущую сессию (ADR-0001).
/// </summary>
public sealed class EngineStatus
{
    public static EngineStatus Stopped { get; } = new();

    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; init; } = EngineContract.Version;

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; init; }

    [JsonPropertyName("mode")]
    [JsonConverter(typeof(ConnectionModeJsonConverter))]
    public ConnectionMode Mode { get; init; } = ConnectionPolicy.DefaultMode;

    [JsonPropertyName("profileId")]
    public string? ProfileId { get; init; }
}
