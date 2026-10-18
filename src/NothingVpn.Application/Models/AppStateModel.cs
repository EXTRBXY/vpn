using System.Text.Json.Serialization;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Models;

/// <summary>
/// Настройки и runtime-флаги клиента. Это же схема state.json: имена JSON-свойств зафиксированы явно
/// и не меняются без миграции (docs/STORAGE.md).
/// </summary>
public sealed class AppStateModel
{
    [JsonPropertyName("activeProfileId")]
    public string? ActiveProfileId { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ConnectionPolicy.DefaultMode;

    [JsonPropertyName("tunAppProcessPaths")]
    public List<string> TunAppProcessPaths { get; set; } = new();

    [JsonPropertyName("userRuleSets")]
    public List<UserRuleSetModel> UserRuleSets { get; set; } = new();

    [JsonPropertyName("localMixedPort")]
    public int LocalMixedPort { get; set; } = ConnectionPolicy.DefaultLocalMixedPort;

    [JsonPropertyName("proxyWasEnabledByUs")]
    public bool ProxyWasEnabledByUs { get; set; }

    [JsonPropertyName("previousProxySettings")]
    public ProxySettingsSnapshotModel? PreviousProxySettings { get; set; }

    [JsonPropertyName("proxyOverride")]
    public string ProxyOverride { get; set; } = ProxyConnectionPolicy.DefaultProxyOverride;

    [JsonPropertyName("tunInterfaceName")]
    public string TunInterfaceName { get; set; } = TunSettingsPolicy.DefaultInterfaceName;

    [JsonPropertyName("tunAddressCidr")]
    public string TunAddressCidr { get; set; } = TunSettingsPolicy.AutoAddressCidr;

    [JsonPropertyName("tunMtu")]
    public int TunMtu { get; set; } = TunSettingsPolicy.DefaultMtu;

    [JsonPropertyName("tunStack")]
    public string TunStack { get; set; } = TunSettingsPolicy.DefaultStack;

    [JsonPropertyName("tunAutoRoute")]
    public bool TunAutoRoute { get; set; } = true;

    [JsonPropertyName("tunStrictRoute")]
    public bool TunStrictRoute { get; set; } = true;

    [JsonPropertyName("dnsMode")]
    public string DnsMode { get; set; } = DnsPolicy.DefaultMode;

    [JsonPropertyName("dohServer")]
    public string DohServer { get; set; } = DnsPolicy.DefaultDohServer;

    [JsonPropertyName("dohPath")]
    public string DohPath { get; set; } = DnsPolicy.DefaultDohPath;

    [JsonPropertyName("dohSni")]
    public string DohSni { get; set; } = DnsPolicy.DefaultDohSni;

    [JsonPropertyName("dnsDetour")]
    public string DnsDetour { get; set; } = DnsPolicy.DefaultDetour;

    [JsonPropertyName("debugLogs")]
    public bool DebugLogs { get; set; }

    [JsonPropertyName("singBoxLogLevel")]
    public string SingBoxLogLevel { get; set; } = SingBoxLogLevelPolicy.DefaultLevel;

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get; set; } = AppCloseBehavior.HideToTray;

    /// <summary>Тег релиза (например v1.2.3), для которого скрыто всплывающее предложение обновиться.</summary>
    [JsonPropertyName("updateDismissedModalForTag")]
    public string? UpdateDismissedModalForTag { get; set; }

    /// <summary>Запомненная версия приложения (X.Y.Z) — для показа списка изменений после обновления.</summary>
    [JsonPropertyName("lastRecordedAppSemver")]
    public string? LastRecordedAppSemver { get; set; }

    /// <summary>Время последней успешной проверки обновлений.</summary>
    [JsonPropertyName("updateLastCheckUtc")]
    public DateTimeOffset? UpdateLastCheckUtc { get; set; }
}
