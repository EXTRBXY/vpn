using NothingVpn.Application.Models;
using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.Store;
using NothingVpn.Tray.Internal.WinInet;

namespace NothingVpn.Infrastructure.Mappers;

internal static class LegacyModelMapper
{
    public static VpnProfile ToModel(VlessProfile source) => new()
    {
        Id = source.Id,
        SubscriptionId = source.SubscriptionId,
        Name = source.Name,
        Uuid = source.Uuid,
        Host = source.Host,
        Port = source.Port,
        Type = source.Type,
        Security = source.Security,
        Encryption = source.Encryption,
        Sni = source.Sni,
        Alpn = source.Alpn.ToList(),
        Fingerprint = source.Fingerprint,
        Flow = source.Flow,
        RealityPublicKey = source.RealityPublicKey,
        RealityShortId = source.RealityShortId,
        WsPath = source.WsPath,
        WsHost = source.WsHost,
        GrpcServiceName = source.GrpcServiceName,
        ExtraQuery = new Dictionary<string, string>(source.ExtraQuery, StringComparer.OrdinalIgnoreCase)
    };

    public static VlessProfile ToLegacy(VpnProfile source) => new()
    {
        Id = source.Id,
        SubscriptionId = source.SubscriptionId,
        Name = source.Name,
        Uuid = source.Uuid,
        Host = source.Host,
        Port = source.Port,
        Type = source.Type,
        Security = source.Security,
        Encryption = source.Encryption,
        Sni = source.Sni,
        Alpn = source.Alpn.ToList(),
        Fingerprint = source.Fingerprint,
        Flow = source.Flow,
        RealityPublicKey = source.RealityPublicKey,
        RealityShortId = source.RealityShortId,
        WsPath = source.WsPath,
        WsHost = source.WsHost,
        GrpcServiceName = source.GrpcServiceName,
        ExtraQuery = new Dictionary<string, string>(source.ExtraQuery, StringComparer.OrdinalIgnoreCase)
    };

    public static AppStateModel ToModel(AppState source) => new()
    {
        ActiveProfileId = source.ActiveProfileId,
        Mode = source.Mode,
        TunAppProcessPaths = source.TunAppProcessPaths.ToList(),
        UserRuleSets = source.UserRuleSets.Select(ToModel).ToList(),
        LocalMixedPort = source.LocalMixedPort,
        ProxyWasEnabledByUs = source.ProxyWasEnabledByUs,
        PreviousProxySettings = source.PreviousProxySettings is null ? null : ToModel(source.PreviousProxySettings),
        ProxyOverride = source.ProxyOverride,
        TunInterfaceName = source.TunInterfaceName,
        TunAddressCidr = source.TunAddressCidr,
        TunMtu = source.TunMtu,
        TunStack = source.TunStack,
        TunAutoRoute = source.TunAutoRoute,
        TunStrictRoute = source.TunStrictRoute,
        DnsMode = source.DnsMode,
        DohServer = source.DohServer,
        DohPath = source.DohPath,
        DohSni = source.DohSni,
        DnsDetour = source.DnsDetour,
        DebugLogs = source.DebugLogs,
        SingBoxLogLevel = source.SingBoxLogLevel,
        UpdateDismissedModalForTag = source.UpdateDismissedModalForTag,
        LastRecordedAppSemver = source.LastRecordedAppSemver,
        UpdateLastCheckUtc = source.UpdateLastCheckUtc
    };

    public static AppState ToLegacy(AppStateModel source) => new()
    {
        ActiveProfileId = source.ActiveProfileId,
        Mode = source.Mode,
        TunAppProcessPaths = source.TunAppProcessPaths.ToList(),
        UserRuleSets = source.UserRuleSets.Select(ToLegacy).ToList(),
        LocalMixedPort = source.LocalMixedPort,
        ProxyWasEnabledByUs = source.ProxyWasEnabledByUs,
        PreviousProxySettings = source.PreviousProxySettings is null ? null : ToLegacy(source.PreviousProxySettings),
        ProxyOverride = source.ProxyOverride,
        TunInterfaceName = source.TunInterfaceName,
        TunAddressCidr = source.TunAddressCidr,
        TunMtu = source.TunMtu,
        TunStack = source.TunStack,
        TunAutoRoute = source.TunAutoRoute,
        TunStrictRoute = source.TunStrictRoute,
        DnsMode = source.DnsMode,
        DohServer = source.DohServer,
        DohPath = source.DohPath,
        DohSni = source.DohSni,
        DnsDetour = source.DnsDetour,
        DebugLogs = source.DebugLogs,
        SingBoxLogLevel = source.SingBoxLogLevel,
        UpdateDismissedModalForTag = source.UpdateDismissedModalForTag,
        LastRecordedAppSemver = source.LastRecordedAppSemver,
        UpdateLastCheckUtc = source.UpdateLastCheckUtc
    };

    public static ProxySettingsSnapshotModel ToModel(WinInetProxySettingsSnapshot source) => new()
    {
        ProxyEnable = source.ProxyEnable,
        ProxyServer = source.ProxyServer,
        ProxyOverride = source.ProxyOverride
    };

    public static WinInetProxySettingsSnapshot ToLegacy(ProxySettingsSnapshotModel source) => new()
    {
        ProxyEnable = source.ProxyEnable,
        ProxyServer = source.ProxyServer,
        ProxyOverride = source.ProxyOverride
    };

    private static UserRuleSetModel ToModel(UserRuleSet source) => new()
    {
        Tag = source.Tag,
        Name = source.Name,
        FileName = source.FileName,
        Enabled = source.Enabled,
        Action = source.Action,
        BuiltinId = source.BuiltinId,
        RemoteEtag = source.RemoteEtag,
        LastDownloadedUtc = source.LastDownloadedUtc
    };

    private static UserRuleSet ToLegacy(UserRuleSetModel source) => new()
    {
        Tag = source.Tag,
        Name = source.Name,
        FileName = source.FileName,
        Enabled = source.Enabled,
        Action = source.Action,
        BuiltinId = source.BuiltinId,
        RemoteEtag = source.RemoteEtag,
        LastDownloadedUtc = source.LastDownloadedUtc
    };
}

