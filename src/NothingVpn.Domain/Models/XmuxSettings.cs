using NothingVpn.Domain.Policies;

namespace NothingVpn.Domain.Models;

/// <summary>XMUX XHTTP по умолчанию: применяется, если в extra профиля нет своего xmux. Значения — диапазоны «N» или «N-M».</summary>
public sealed class XmuxSettings
{
    public string MaxConcurrency { get; set; } = XhttpPolicy.DefaultXmuxMaxConcurrency;
    public string MaxConnections { get; set; } = XhttpPolicy.DefaultXmuxMaxConnections;
    public string CMaxReuseTimes { get; set; } = XhttpPolicy.DefaultXmuxCMaxReuseTimes;
    public string HMaxRequestTimes { get; set; } = XhttpPolicy.DefaultXmuxHMaxRequestTimes;
    public string HMaxReusableSecs { get; set; } = XhttpPolicy.DefaultXmuxHMaxReusableSecs;
}
