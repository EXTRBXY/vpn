using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

/// <summary>
/// Проверка XHTTP-полей профиля Domain-политикой. Вызывается при импорте ссылки и в ядре при приёме запроса:
/// служба не доверяет разбору ссылки в интерфейсе.
/// </summary>
public static class XhttpProfileValidator
{
    public static void Validate(VpnProfile profile)
    {
        if (!XhttpPolicy.IsXhttp(profile.Type))
            return;

        XhttpPolicy.EnsureText(profile.XhttpPath, "path");
        XhttpPolicy.EnsureText(profile.XhttpHost, "host");
        if (!XhttpPolicy.Modes.IsDefined(profile.XhttpMode))
            throw new ArgumentException("XHTTP: неизвестный mode.");

        if (profile.XhttpExtra is { } extra)
            Validate(extra);
    }

    private static void Validate(XhttpExtraModel extra)
    {
        XhttpPolicy.EnsureHeaders(extra.Headers);

        XhttpPolicy.EnsureRange(extra.XPaddingBytes, "xPaddingBytes");
        XhttpPolicy.EnsureRange(extra.SessionIdLength, "sessionIDLength");
        XhttpPolicy.EnsureRange(extra.UplinkChunkSize, "uplinkChunkSize");
        XhttpPolicy.EnsureRange(extra.ScMaxEachPostBytes, "scMaxEachPostBytes");
        XhttpPolicy.EnsureRange(extra.ScMinPostsIntervalMs, "scMinPostsIntervalMs");

        XhttpPolicy.EnsureText(extra.XPaddingKey, "xPaddingKey");
        XhttpPolicy.EnsureText(extra.XPaddingHeader, "xPaddingHeader");
        XhttpPolicy.EnsureText(extra.XPaddingPlacement, "xPaddingPlacement");
        XhttpPolicy.EnsureText(extra.XPaddingMethod, "xPaddingMethod");
        XhttpPolicy.EnsureText(extra.UplinkHttpMethod, "uplinkHTTPMethod");
        XhttpPolicy.EnsureText(extra.SessionIdPlacement, "sessionIDPlacement");
        XhttpPolicy.EnsureText(extra.SessionIdKey, "sessionIDKey");
        XhttpPolicy.EnsureText(extra.SessionIdTable, "sessionIDTable");
        XhttpPolicy.EnsureText(extra.SeqPlacement, "seqPlacement");
        XhttpPolicy.EnsureText(extra.SeqKey, "seqKey");
        XhttpPolicy.EnsureText(extra.UplinkDataPlacement, "uplinkDataPlacement");
        XhttpPolicy.EnsureText(extra.UplinkDataKey, "uplinkDataKey");

        if (extra.Xmux is not { } xmux)
            return;

        XhttpPolicy.EnsureRange(xmux.MaxConcurrency, "xmux.maxConcurrency");
        XhttpPolicy.EnsureRange(xmux.MaxConnections, "xmux.maxConnections");
        XhttpPolicy.EnsureRange(xmux.CMaxReuseTimes, "xmux.cMaxReuseTimes");
        XhttpPolicy.EnsureRange(xmux.HMaxRequestTimes, "xmux.hMaxRequestTimes");
        XhttpPolicy.EnsureRange(xmux.HMaxReusableSecs, "xmux.hMaxReusableSecs");
        if (xmux.MaxConcurrency is not null && !XhttpPolicy.IsZeroRange(xmux.MaxConcurrency)
            && xmux.MaxConnections is not null && !XhttpPolicy.IsZeroRange(xmux.MaxConnections))
            throw new ArgumentException("XHTTP: xmux.maxConcurrency и xmux.maxConnections нельзя задавать одновременно.");
    }
}
