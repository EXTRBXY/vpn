namespace NothingVpn.Domain.Models;

/// <summary>Движок, который соединяется с сервером профиля (ADR-0002). Входом всегда остаётся sing-box.</summary>
public enum OutboundEngine
{
    Auto,
    SingBox,
    Xray
}
