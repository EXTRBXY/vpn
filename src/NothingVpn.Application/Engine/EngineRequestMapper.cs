using NothingVpn.Application.Models;

namespace NothingVpn.Application.Engine;

/// <summary>
/// Перевод между моделями хранения и контрактом ядра. <see cref="CreateRequest"/> вызывает интерфейс,
/// <see cref="ToProfile"/>/<see cref="ToState"/> — ядро, чтобы передать запрос генератору конфига: генератор
/// по-прежнему работает с <see cref="VpnProfile"/> и <see cref="AppStateModel"/>, поэтому итоговый JSON
/// sing-box не зависит от того, прошли ли настройки через контракт.
/// </summary>
public static class EngineRequestMapper
{
    public static EngineConnectRequest CreateRequest(VpnProfile profile, AppStateModel state) => new()
    {
        Profile = new EngineProfile
        {
            Id = profile.Id,
            Uuid = profile.Uuid,
            Host = profile.Host,
            Port = profile.Port,
            Type = profile.Type,
            Security = profile.Security,
            Sni = profile.Sni,
            Alpn = [.. profile.Alpn],
            Fingerprint = profile.Fingerprint,
            Flow = profile.Flow,
            RealityPublicKey = profile.RealityPublicKey,
            RealityShortId = profile.RealityShortId,
            WsPath = profile.WsPath,
            WsHost = profile.WsHost,
            GrpcServiceName = profile.GrpcServiceName,
            Engine = profile.Engine,
            XhttpPath = profile.XhttpPath,
            XhttpHost = profile.XhttpHost,
            XhttpMode = profile.XhttpMode,
            XhttpExtra = profile.XhttpExtra
        },
        Mode = state.Mode,
        LogLevel = state.SingBoxLogLevel,
        LocalMixedPort = state.LocalMixedPort,
        Dns = new EngineDnsSettings
        {
            Mode = state.DnsMode,
            DohServer = state.DohServer,
            DohPath = state.DohPath,
            DohSni = state.DohSni,
            Detour = state.DnsDetour
        },
        Tun = new EngineTunSettings
        {
            InterfaceName = state.TunInterfaceName,
            AddressCidr = state.TunAddressCidr,
            Mtu = state.TunMtu,
            Stack = state.TunStack,
            AutoRoute = state.TunAutoRoute,
            StrictRoute = state.TunStrictRoute
        },
        Xmux = new EngineXmuxSettings
        {
            MaxConcurrency = state.XhttpXmuxMaxConcurrency,
            MaxConnections = state.XhttpXmuxMaxConnections,
            CMaxReuseTimes = state.XhttpXmuxCMaxReuseTimes,
            HMaxRequestTimes = state.XhttpXmuxHMaxRequestTimes,
            HMaxReusableSecs = state.XhttpXmuxHMaxReusableSecs
        },
        DomainExceptions = state.DomainExceptions.Where(x => x.Enabled).Select(x => x.Copy()).ToList(),
        TunAppProcessPaths = [.. state.TunAppProcessPaths],
        RuleSets = state.UserRuleSets
            .Where(rs => rs.Enabled)
            .Select(rs => new EngineRuleSet { Tag = rs.Tag, FileName = rs.FileName, Action = rs.Action })
            .ToList()
    };

    public static VpnProfile ToProfile(EngineConnectRequest request)
    {
        var p = request.Profile ?? new EngineProfile();
        return new VpnProfile
        {
            Id = p.Id ?? string.Empty,
            Uuid = p.Uuid ?? string.Empty,
            Host = p.Host ?? string.Empty,
            Port = p.Port,
            Type = p.Type ?? string.Empty,
            Security = p.Security ?? string.Empty,
            Sni = p.Sni,
            Alpn = [.. p.Alpn ?? []],
            Fingerprint = p.Fingerprint,
            Flow = p.Flow,
            RealityPublicKey = p.RealityPublicKey,
            RealityShortId = p.RealityShortId,
            WsPath = p.WsPath,
            WsHost = p.WsHost,
            GrpcServiceName = p.GrpcServiceName,
            Engine = p.Engine,
            XhttpPath = p.XhttpPath,
            XhttpHost = p.XhttpHost,
            XhttpMode = p.XhttpMode,
            XhttpExtra = p.XhttpExtra
        };
    }

    /// <summary>Состояние только с полями, которые читает генератор; остальные — значения по умолчанию.</summary>
    public static AppStateModel ToState(EngineConnectRequest request)
    {
        var dns = request.Dns ?? new EngineDnsSettings();
        var tun = request.Tun ?? new EngineTunSettings();
        var xmux = request.Xmux ?? new EngineXmuxSettings();
        return new AppStateModel
        {
            Mode = request.Mode,
            SingBoxLogLevel = request.LogLevel,
            LocalMixedPort = request.LocalMixedPort,
            DnsMode = dns.Mode,
            DohServer = dns.DohServer ?? string.Empty,
            DohPath = dns.DohPath ?? string.Empty,
            DohSni = dns.DohSni ?? string.Empty,
            DnsDetour = dns.Detour,
            TunInterfaceName = tun.InterfaceName ?? string.Empty,
            TunAddressCidr = tun.AddressCidr ?? string.Empty,
            TunMtu = tun.Mtu,
            TunStack = tun.Stack,
            TunAutoRoute = tun.AutoRoute,
            TunStrictRoute = tun.StrictRoute,
            XhttpXmuxMaxConcurrency = xmux.MaxConcurrency ?? string.Empty,
            XhttpXmuxMaxConnections = xmux.MaxConnections ?? string.Empty,
            XhttpXmuxCMaxReuseTimes = xmux.CMaxReuseTimes ?? string.Empty,
            XhttpXmuxHMaxRequestTimes = xmux.HMaxRequestTimes ?? string.Empty,
            XhttpXmuxHMaxReusableSecs = xmux.HMaxReusableSecs ?? string.Empty,
            DomainExceptions = (request.DomainExceptions ?? []).Select(x => x.Copy()).ToList(),
            TunAppProcessPaths = [.. request.TunAppProcessPaths ?? []],
            UserRuleSets = (request.RuleSets ?? [])
                .Select(rs => new UserRuleSetModel
                {
                    Tag = rs.Tag ?? string.Empty,
                    Name = rs.Tag ?? string.Empty,
                    FileName = rs.FileName ?? string.Empty,
                    Action = rs.Action,
                    Enabled = true
                })
                .ToList()
        };
    }
}
