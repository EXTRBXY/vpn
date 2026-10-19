using System.Text.Json.Serialization;
using NothingVpn.Application.Serialization;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Engine;

/// <summary>
/// Запрос на подключение — будущее сообщение <c>Connect</c> канала службы. Только то, что нужно ядру для конфига:
/// ни готового JSON sing-box, ни путей, куда ядро будет писать. Ядро не доверяет нормализации на стороне
/// интерфейса и само валидирует запрос Domain-политиками. Имена JSON и строки enum'ов — часть контракта
/// (<see cref="EngineContract.Version"/>), строки enum'ов совпадают с state.json.
/// </summary>
public sealed class EngineConnectRequest
{
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; set; } = EngineContract.Version;

    [JsonPropertyName("profile")]
    public EngineProfile Profile { get; set; } = new();

    [JsonPropertyName("mode")]
    [JsonConverter(typeof(ConnectionModeJsonConverter))]
    public ConnectionMode Mode { get; set; } = ConnectionPolicy.DefaultMode;

    [JsonPropertyName("logLevel")]
    [JsonConverter(typeof(LogLevelJsonConverter))]
    public LogLevel LogLevel { get; set; } = SingBoxLogLevelPolicy.DefaultLevel;

    /// <summary>Порт mixed-inbound на 127.0.0.1 (режим proxy). Системный прокси на него включает интерфейс.</summary>
    [JsonPropertyName("localMixedPort")]
    public int LocalMixedPort { get; set; } = ConnectionPolicy.DefaultLocalMixedPort;

    [JsonPropertyName("dns")]
    public EngineDnsSettings Dns { get; set; } = new();

    [JsonPropertyName("tun")]
    public EngineTunSettings Tun { get; set; } = new();

    /// <summary>Полные пути .exe для режима tun_apps.</summary>
    [JsonPropertyName("tunAppProcessPaths")]
    public List<string> TunAppProcessPaths { get; set; } = new();

    /// <summary>Только включённые rule-set, в порядке пользователя (порядок route-правил значим).</summary>
    [JsonPropertyName("ruleSets")]
    public List<EngineRuleSet> RuleSets { get; set; } = new();
}

/// <summary>Параметры VLESS-профиля, которые попадают в outbound и адреса TUN.</summary>
public sealed class EngineProfile
{
    /// <summary>VlessLinkParser.StableId: от него зависят имя и адрес TUN-интерфейса.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 443;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "tcp";

    [JsonPropertyName("security")]
    public string Security { get; set; } = "tls";

    [JsonPropertyName("sni")]
    public string? Sni { get; set; }

    [JsonPropertyName("alpn")]
    public List<string> Alpn { get; set; } = new();

    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("flow")]
    public string? Flow { get; set; }

    [JsonPropertyName("realityPublicKey")]
    public string? RealityPublicKey { get; set; }

    [JsonPropertyName("realityShortId")]
    public string? RealityShortId { get; set; }

    [JsonPropertyName("wsPath")]
    public string? WsPath { get; set; }

    [JsonPropertyName("wsHost")]
    public string? WsHost { get; set; }

    [JsonPropertyName("grpcServiceName")]
    public string? GrpcServiceName { get; set; }
}

public sealed class EngineDnsSettings
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(DnsModeJsonConverter))]
    public DnsMode Mode { get; set; } = DnsPolicy.DefaultMode;

    [JsonPropertyName("dohServer")]
    public string DohServer { get; set; } = DnsPolicy.DefaultDohServer;

    [JsonPropertyName("dohPath")]
    public string DohPath { get; set; } = DnsPolicy.DefaultDohPath;

    [JsonPropertyName("dohSni")]
    public string DohSni { get; set; } = DnsPolicy.DefaultDohSni;

    [JsonPropertyName("detour")]
    [JsonConverter(typeof(DnsDetourJsonConverter))]
    public DnsDetour Detour { get; set; } = DnsPolicy.DefaultDetour;
}

public sealed class EngineTunSettings
{
    [JsonPropertyName("interfaceName")]
    public string InterfaceName { get; set; } = TunSettingsPolicy.DefaultInterfaceName;

    [JsonPropertyName("addressCidr")]
    public string AddressCidr { get; set; } = TunSettingsPolicy.AutoAddressCidr;

    [JsonPropertyName("mtu")]
    public int Mtu { get; set; } = TunSettingsPolicy.DefaultMtu;

    [JsonPropertyName("stack")]
    [JsonConverter(typeof(TunStackJsonConverter))]
    public TunStack Stack { get; set; } = TunSettingsPolicy.DefaultStack;

    [JsonPropertyName("autoRoute")]
    public bool AutoRoute { get; set; } = true;

    [JsonPropertyName("strictRoute")]
    public bool StrictRoute { get; set; } = true;
}

/// <summary>
/// Rule-set запроса. <see cref="FileName"/> — простое имя .srs без пути (проверяет <see cref="RuleSetPolicyValidator"/>).
/// Внутри процесса ядро берёт файл с этим именем из общего каталога rulesets пользователя, а <see cref="Content"/>
/// не заполняется. Служба (контракт v2, ADR-0001) путей пользователя не читает: клиент канала передаёт
/// содержимое файла (не больше <see cref="RuleSetPolicyValidator.MaxContentBytes"/>), служба кладёт его в свой
/// каталог под своим именем (<see cref="IEngineRuleSetStore"/>).
/// </summary>
public sealed class EngineRuleSet
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    [JsonConverter(typeof(RuleSetActionJsonConverter))]
    public RuleSetAction Action { get; set; } = RuleSetPolicyValidator.DefaultAction;

    /// <summary>Содержимое .srs (в JSON — base64). Обязательно для службы, внутри процесса не используется.</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? Content { get; set; }
}
