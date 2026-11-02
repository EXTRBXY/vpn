using System.Text.Json.Serialization;
using NothingVpn.Application.Serialization;

namespace NothingVpn.Application.Models;

/// <summary>
/// Клиентские поля <c>extra</c> XHTTP (SplitHTTPConfig Xray v26.10.31). Имена JSON совпадают с Xray: модель — и схема
/// profiles.json, и блок <c>extra</c> конфига Xray. Диапазоны — строки «N» или «N-M» (XhttpPolicy).
/// </summary>
public sealed class XhttpExtraModel
{
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("xPaddingBytes")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? XPaddingBytes { get; set; }

    [JsonPropertyName("xPaddingObfsMode")]
    public bool? XPaddingObfsMode { get; set; }

    [JsonPropertyName("xPaddingKey")]
    public string? XPaddingKey { get; set; }

    [JsonPropertyName("xPaddingHeader")]
    public string? XPaddingHeader { get; set; }

    [JsonPropertyName("xPaddingPlacement")]
    public string? XPaddingPlacement { get; set; }

    [JsonPropertyName("xPaddingMethod")]
    public string? XPaddingMethod { get; set; }

    [JsonPropertyName("uplinkHTTPMethod")]
    public string? UplinkHttpMethod { get; set; }

    [JsonPropertyName("sessionIDPlacement")]
    public string? SessionIdPlacement { get; set; }

    [JsonPropertyName("sessionIDKey")]
    public string? SessionIdKey { get; set; }

    [JsonPropertyName("sessionIDTable")]
    public string? SessionIdTable { get; set; }

    [JsonPropertyName("sessionIDLength")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? SessionIdLength { get; set; }

    [JsonPropertyName("seqPlacement")]
    public string? SeqPlacement { get; set; }

    [JsonPropertyName("seqKey")]
    public string? SeqKey { get; set; }

    [JsonPropertyName("uplinkDataPlacement")]
    public string? UplinkDataPlacement { get; set; }

    [JsonPropertyName("uplinkDataKey")]
    public string? UplinkDataKey { get; set; }

    [JsonPropertyName("uplinkChunkSize")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? UplinkChunkSize { get; set; }

    [JsonPropertyName("noGRPCHeader")]
    public bool? NoGrpcHeader { get; set; }

    [JsonPropertyName("scMaxEachPostBytes")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? ScMaxEachPostBytes { get; set; }

    [JsonPropertyName("scMinPostsIntervalMs")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? ScMinPostsIntervalMs { get; set; }

    [JsonPropertyName("xmux")]
    public XmuxModel? Xmux { get; set; }
}

public sealed class XmuxModel
{
    [JsonPropertyName("maxConcurrency")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? MaxConcurrency { get; set; }

    [JsonPropertyName("maxConnections")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? MaxConnections { get; set; }

    [JsonPropertyName("cMaxReuseTimes")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? CMaxReuseTimes { get; set; }

    [JsonPropertyName("hMaxRequestTimes")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? HMaxRequestTimes { get; set; }

    [JsonPropertyName("hMaxReusableSecs")]
    [JsonConverter(typeof(XrayRangeJsonConverter))]
    public string? HMaxReusableSecs { get; set; }

    /// <summary>Секунды; единственное поле XMUX без диапазона, отрицательное значение отключает keep-alive.</summary>
    [JsonPropertyName("hKeepAlivePeriod")]
    public long? HKeepAlivePeriod { get; set; }
}
