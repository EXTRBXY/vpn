using System.Globalization;
using System.Text.RegularExpressions;
using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

/// <summary>
/// Транспорт XHTTP Xray (ADR-0002): режимы, проверка значений extra и XMUX по умолчанию. Значения XMUX — те, что Xray
/// берёт сам, когда XMUX не задан вовсе; в конфиг они пишутся явно, потому что любое заданное поле XMUX отключает
/// значения по умолчанию остальных.
/// </summary>
public static partial class XhttpPolicy
{
    public const string Transport = "xhttp";
    public const string LegacyTransport = "splithttp";
    public const XhttpMode DefaultMode = XhttpMode.Auto;

    public const string DefaultXmuxMaxConcurrency = "16-32";
    public const string DefaultXmuxMaxConnections = "0";
    public const string DefaultXmuxCMaxReuseTimes = "0";
    public const string DefaultXmuxHMaxRequestTimes = "600-900";
    public const string DefaultXmuxHMaxReusableSecs = "1800-3000";

    public const int MaxHeaders = 32;
    public const int MaxTextLength = 1024;

    public static readonly SettingValueMap<XhttpMode> Modes = new(
        DefaultMode,
        new Dictionary<XhttpMode, string>
        {
            [XhttpMode.Auto] = "auto",
            [XhttpMode.PacketUp] = "packet-up",
            [XhttpMode.StreamUp] = "stream-up",
            [XhttpMode.StreamOne] = "stream-one"
        });

    [GeneratedRegex(@"^(\d{1,9})(?:-(\d{1,9}))?$")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderNamePattern();

    public static bool IsXhttp(string? transport) =>
        string.Equals(transport, Transport, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>splithttp</c> — прежнее имя XHTTP в ссылках.</summary>
    public static string NormalizeTransport(string transport) =>
        string.Equals(transport, LegacyTransport, StringComparison.OrdinalIgnoreCase) ? Transport : transport;

    /// <summary>Неотрицательный диапазон Xray: «N» или «N-M», N ≤ M.</summary>
    public static bool IsValidRange(string? value)
    {
        var match = RangePattern().Match(value ?? string.Empty);
        if (!match.Success)
            return false;
        if (!match.Groups[2].Success)
            return true;
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            <= int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    public static bool IsZeroRange(string? value) => value?.Trim() is "0" or "0-0";

    public static void EnsureRange(string? value, string field)
    {
        if (value is not null && !IsValidRange(value))
            throw new ArgumentException($"XHTTP: «{field}» должно быть числом или диапазоном «N-M».");
    }

    public static void EnsureText(string? value, string field)
    {
        if (value is null)
            return;
        if (value.Length > MaxTextLength || value.Any(char.IsControl))
            throw new ArgumentException($"XHTTP: недопустимое значение «{field}».");
    }

    public static void EnsureHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
            return;
        if (headers.Count > MaxHeaders)
            throw new ArgumentException($"XHTTP: заголовков больше {MaxHeaders}.");
        foreach (var (name, value) in headers)
        {
            if (!HeaderNamePattern().IsMatch(name))
                throw new ArgumentException("XHTTP: недопустимое имя заголовка.");
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("XHTTP: Host задаётся параметром host, а не заголовком.");
            EnsureText(value, name);
        }
    }

    /// <summary>Толерантно для чтения state.json: пустое или неверное значение — значение по умолчанию поля.</summary>
    public static void Normalize(XmuxSettings settings)
    {
        settings.MaxConcurrency = NormalizeRange(settings.MaxConcurrency, DefaultXmuxMaxConcurrency);
        settings.MaxConnections = NormalizeRange(settings.MaxConnections, DefaultXmuxMaxConnections);
        settings.CMaxReuseTimes = NormalizeRange(settings.CMaxReuseTimes, DefaultXmuxCMaxReuseTimes);
        settings.HMaxRequestTimes = NormalizeRange(settings.HMaxRequestTimes, DefaultXmuxHMaxRequestTimes);
        settings.HMaxReusableSecs = NormalizeRange(settings.HMaxReusableSecs, DefaultXmuxHMaxReusableSecs);
        if (!IsZeroRange(settings.MaxConcurrency) && !IsZeroRange(settings.MaxConnections))
            settings.MaxConnections = DefaultXmuxMaxConnections;
    }

    public static void Validate(XmuxSettings settings)
    {
        EnsureXmuxRange(settings.MaxConcurrency, "maxConcurrency");
        EnsureXmuxRange(settings.MaxConnections, "maxConnections");
        EnsureXmuxRange(settings.CMaxReuseTimes, "cMaxReuseTimes");
        EnsureXmuxRange(settings.HMaxRequestTimes, "hMaxRequestTimes");
        EnsureXmuxRange(settings.HMaxReusableSecs, "hMaxReusableSecs");
        if (!IsZeroRange(settings.MaxConcurrency) && !IsZeroRange(settings.MaxConnections))
            throw new ArgumentException("XMUX: maxConcurrency и maxConnections нельзя задавать одновременно, одно из них должно быть 0.");
    }

    private static void EnsureXmuxRange(string? value, string field)
    {
        if (!IsValidRange(value?.Trim()))
            throw new ArgumentException($"XMUX: «{field}» должно быть числом или диапазоном «N-M».");
    }

    private static string NormalizeRange(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return IsValidRange(trimmed) ? trimmed! : fallback;
    }
}
