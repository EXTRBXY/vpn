using System.Text.Json;
using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.SingBox;

/// <summary>
/// Переводит профиль и состояние в конфиг sing-box. Состояние должно быть нормализовано
/// AppStateNormalizer (так делает VpnConnectionService); значения здесь не нормализуются повторно,
/// а только проверяются там, где ошибка дала бы некорректный конфиг.
/// </summary>
internal static class SingBoxConfigGenerator
{
    public static string WriteConfig(AppPaths paths, VpnProfile profile, AppStateModel state)
    {
        var config = Build(paths, profile, state);
        var json = JsonSerializer.Serialize(config, JsonOptions());

        var path = Path.Combine(paths.ConfigsDir, $"{profile.Id}.{state.Mode.ToValue()}.json");
        File.WriteAllText(path, json);
        return path;
    }

    public static SingBoxConfig Build(AppPaths paths, VpnProfile profile, AppStateModel state)
    {
        var mode = state.Mode;
        var useTun = ConnectionPolicy.IsTunMode(mode);
        var inbounds = useTun
            ? new List<SingBoxInbound> { BuildTunInbound(state, profile, mode) }
            : new List<SingBoxInbound> { BuildMixedInbound(state.LocalMixedPort) };

        var outbound = new SingBoxOutbound
        {
            Type = "vless",
            Tag = "proxy",
            Server = profile.Host,
            ServerPort = profile.Port,
            Uuid = profile.Uuid,
            Flow = profile.Flow,
            Tls = BuildTls(profile)
        };

        outbound.Transport = BuildTransport(profile);

        var routing = EvaluateRoutingPolicy(state, useTun);
        var useDohResolver = state.DnsMode == DnsMode.Doh;
        var dnsDetour = ResolveDnsDetour(state, mode, useTun);
        if (dnsDetour == "proxy")
            outbound.DomainResolver = TunBootstrapPolicy.BootstrapLocalDnsTag;

        return new SingBoxConfig
        {
            Log = new SingBoxLog { Level = state.SingBoxLogLevel.ToValue() },
            Dns = BuildDns(state, useTun, profile, routing, dnsDetour),
            Inbounds = inbounds,
            Outbounds = new List<SingBoxOutbound>
            {
                outbound,
                new SingBoxOutbound { Type = "direct", Tag = "direct" },
                new SingBoxOutbound { Type = "block", Tag = "block" }
            },
            Route = BuildRoute(paths, mode, state, profile, useDohResolver)
        };
    }

    private static RuleSetRoutingDecision EvaluateRoutingPolicy(AppStateModel state, bool useTun) =>
        RuleSetRoutingPolicy.Evaluate(ConnectionSettingsMapper.ToRuleSetEntries(state.UserRuleSets), state.DnsMode, useTun);

    private static string? ResolveDnsDetour(AppStateModel state, ConnectionMode mode, bool useTun)
    {
        if (!DnsDetourPolicy.AllowsProxyDetour(mode))
            return null;

        var requestedProxy = DnsDetourPolicy.EffectiveDetour(mode, state.DnsDetour) == DnsDetour.Proxy;
        if (!useTun)
            return requestedProxy ? "proxy" : null;

        return TunBootstrapPolicy.ResolveSingBoxDohDetour(
            mode,
            TunAppsPolicy.UseStrictRoute(mode, state.TunStrictRoute),
            requestedProxy ? DnsDetour.Proxy : DnsDetour.Direct);
    }

    private static List<SingBoxRouteRule> BuildTunHeadRules(VpnProfile profile, ConnectionMode mode)
    {
        var rules = new List<SingBoxRouteRule>
        {
            new() { Action = "sniff" },
            new() { IpIsPrivate = true, Action = "route", Outbound = "direct" }
        };

        if (TunRoutingPolicy.HijackDns(mode))
        {
            rules.Insert(1, new SingBoxRouteRule
            {
                Protocol = "dns",
                Action = "hijack-dns"
            });
        }

        foreach (var domain in TunBootstrapPolicy.CollectEndpointDomains(profile.Host))
        {
            rules.Add(new SingBoxRouteRule
            {
                Domain = new List<string> { domain },
                Action = "route",
                Outbound = "direct"
            });
        }

        if (TunRoutingPolicy.RouteSecureDnsThroughProxy(mode))
        {
            rules.Add(new SingBoxRouteRule
            {
                Domain = [.. TunRoutingPolicy.KnownSecureDnsDomains],
                Action = "route",
                Outbound = "proxy"
            });
        }

        return rules;
    }

    private static void AddTunFallbackRules(List<SingBoxRouteRule> rules, ConnectionMode mode)
    {
        // Rule evaluation is first-match-wins. These broad protocol/address rules must
        // stay after domain rule-sets, otherwise a direct/block .srs entry is bypassed
        // whenever the same site happens to use HTTP/3 or resolves to IPv6.
        if (TunRoutingPolicy.RouteQuicThroughProxy(mode))
        {
            rules.Add(new SingBoxRouteRule
            {
                Protocol = "quic",
                Action = "route",
                Outbound = "proxy"
            });
        }

        if (TunRoutingPolicy.RouteIpv6ThroughProxy(mode))
        {
            rules.Add(new SingBoxRouteRule
            {
                IpVersion = 6,
                Action = "route",
                Outbound = "proxy"
            });
        }
    }

    private static SingBoxRoute BuildRoute(
        AppPaths paths,
        ConnectionMode mode,
        AppStateModel state,
        VpnProfile profile,
        bool useDohResolver)
    {
        var useTun = ConnectionPolicy.IsTunMode(mode);
        var defaultResolver = TunBootstrapPolicy.ResolveDefaultDomainResolver(useTun, useDohResolver);

        var policy = BuildUserRuleSetPolicy(paths, state);
        var userRuleSets = policy.RuleSets;
        var userRules = policy.Rules;

        if (!useTun)
        {
            var proxyRules = new List<SingBoxRouteRule>
            {
                new() { Action = "sniff" },
                new() { Protocol = "stun", Action = "reject" }
            };
            proxyRules.AddRange(userRules);

            return new SingBoxRoute
            {
                Final = "proxy",
                AutoDetectInterface = false,
                DefaultDomainResolver = useDohResolver ? "doh" : null,
                RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
                Rules = proxyRules
            };
        }

        var rules = BuildTunHeadRules(profile, mode);
        rules.Add(new SingBoxRouteRule { Protocol = "stun", Action = "reject" });
        rules.AddRange(userRules);
        AddTunFallbackRules(rules, mode);

        if (mode == ConnectionMode.TunApps)
        {
            var procPaths = state.TunAppProcessPaths;
            if (procPaths.Count == 0)
                throw new ArgumentException("tun_apps requires at least one process path.");

            var procNames = DeriveProcessNames(procPaths);
            rules.Add(new SingBoxRouteRule
            {
                ProcessPath = procPaths,
                Action = "route",
                Outbound = "proxy"
            });
            if (procNames.Count > 0)
            {
                rules.Add(new SingBoxRouteRule
                {
                    ProcessName = procNames,
                    Action = "route",
                    Outbound = "proxy"
                });
            }

            return new SingBoxRoute
            {
                Final = "direct",
                AutoDetectInterface = true,
                DefaultDomainResolver = defaultResolver,
                RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
                Rules = rules
            };
        }

        return new SingBoxRoute
        {
            Final = "proxy",
            AutoDetectInterface = true,
            DefaultDomainResolver = defaultResolver,
            RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
            Rules = rules
        };
    }

    private static (List<SingBoxRuleSet> RuleSets, List<SingBoxRouteRule> Rules) BuildUserRuleSetPolicy(AppPaths paths, AppStateModel state)
    {
        var ruleSets = new List<SingBoxRuleSet>();
        var rules = new List<SingBoxRouteRule>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in state.UserRuleSets)
        {
            if (!rs.Enabled) continue;
            var tag = rs.Tag.Trim();
            if (tag.Length == 0) continue;
            if (!seenTags.Add(tag)) continue;

            var fileName = rs.FileName.Trim();
            if (!RuleSetPolicyValidator.IsSafeRuleSetFileName(fileName))
                throw new ArgumentException("User rule-set file name must be a plain file name.");
            if (!fileName.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("User rule-set file must be .srs.");

            var fullPath = Path.Combine(paths.RuleSetsDir, fileName);

            ruleSets.Add(new SingBoxRuleSet
            {
                Type = "local",
                Tag = tag,
                Path = fullPath,
                Format = "binary"
            });

            if (!RuleSetPolicyValidator.Actions.IsDefined(rs.Action))
                throw new ArgumentException("User rule-set action must be direct|block.");
            var outbound = rs.Action == RuleSetAction.Block ? "block" : "direct";

            rules.Add(new SingBoxRouteRule
            {
                RuleSet = new List<string> { tag },
                Action = "route",
                Outbound = outbound
            });
        }

        return (ruleSets, rules);
    }

    internal static List<string> DeriveProcessNames(IEnumerable<string> processPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in processPaths)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) continue;
            set.Add(name);
        }

        return set.ToList();
    }

    private static SingBoxInbound BuildMixedInbound(int localMixedPort) => new()
    {
        Type = "mixed",
        Tag = "mixed-in",
        Listen = "127.0.0.1",
        ListenPort = localMixedPort
    };

    private static SingBoxInbound BuildTunInbound(AppStateModel state, VpnProfile profile, ConnectionMode mode) => new()
    {
        Type = "tun",
        Tag = "tun-in",
        InterfaceName = TunInterfacePolicy.InterfaceName(state.TunInterfaceName, profile.Id),
        Address = new List<string>
        {
            TunInterfacePolicy.Ipv4Address(state.TunAddressCidr, profile.Id),
            TunInterfacePolicy.Ipv6Address(profile.Id)
        },
        Mtu = state.TunMtu,
        AutoRoute = state.TunAutoRoute,
        StrictRoute = TunAppsPolicy.UseStrictRoute(mode, state.TunStrictRoute),
        Stack = TunInterfacePolicy.SingBoxStack(state.TunStack)
    };

    private static SingBoxDns? BuildDns(
        AppStateModel state,
        bool useTun,
        VpnProfile profile,
        RuleSetRoutingDecision routing,
        string? dnsDetour)
    {
        var useDoh = state.DnsMode == DnsMode.Doh;
        var hasEnabledUserRuleSets = state.UserRuleSets.Any(x => x.Enabled);
        if (!useTun && !useDoh && !hasEnabledUserRuleSets) return null;

        if (useDoh)
        {
            // Пустой сервер в нормализованном состоянии возможен только у файла, записанного не клиентом:
            // UI не сохраняет DoH без сервера (DnsPolicy.Validate).
            var server = state.DohServer.Length == 0 ? DnsPolicy.DefaultDohServer : state.DohServer;
            var path = state.DohPath;
            var sni = state.DohSni;
            if (sni.Length == 0)
                throw new ArgumentException("DoH SNI is required.");

            var bootstrapDomains = useTun
                ? TunBootstrapPolicy.CollectEndpointDomains(profile.Host)
                : Array.Empty<string>();

            var servers = new List<SingBoxDnsServer>();
            if (useTun || routing.RequiresSplitDns || dnsDetour == "proxy")
            {
                servers.Add(new SingBoxDnsServer
                {
                    Type = "local",
                    Tag = TunBootstrapPolicy.BootstrapLocalDnsTag,
                    PreferGo = false
                });
            }

            servers.Add(new SingBoxDnsServer
            {
                Type = "https",
                Tag = "doh",
                Server = server,
                ServerPort = 443,
                Path = path,
                Tls = new SingBoxDnsTls { Enabled = true, ServerName = sni },
                Detour = dnsDetour
            });

            var rules = new List<SingBoxDnsRule>();
            rules.AddRange(bootstrapDomains.Select(domain => new SingBoxDnsRule
            {
                Domain = new List<string> { domain },
                Server = TunBootstrapPolicy.BootstrapLocalDnsTag
            }));
            rules.AddRange(routing.DnsDirectRuleSetTags.Select(tag => new SingBoxDnsRule
            {
                RuleSet = new List<string> { tag },
                Server = TunBootstrapPolicy.BootstrapLocalDnsTag
            }));

            return new SingBoxDns
            {
                Final = "doh",
                ReverseMapping = useTun,
                Strategy = useTun ? "ipv4_only" : null,
                Servers = servers,
                Rules = rules.Count == 0 ? null : rules
            };
        }

        return BuildTunSystemDns(profile, reverseMapping: useTun);
    }

    private static SingBoxDns BuildTunSystemDns(VpnProfile profile, bool reverseMapping)
    {
        var bootstrapDomains = TunBootstrapPolicy.CollectEndpointDomains(profile.Host);
        const string tag = TunBootstrapPolicy.BootstrapLocalDnsTag;

        return new SingBoxDns
        {
            Final = tag,
            ReverseMapping = reverseMapping ? true : null,
            Strategy = "ipv4_only",
            Servers =
            [
                new SingBoxDnsServer
                {
                    Type = "local",
                    Tag = tag,
                    PreferGo = false
                }
            ],
            Rules = bootstrapDomains.Count > 0
                ? bootstrapDomains.Select(d => new SingBoxDnsRule
                {
                    Domain = new List<string> { d },
                    Server = tag
                }).ToList()
                : null
        };
    }

    private static SingBoxTls? BuildTls(VpnProfile p)
    {
        if (p.Security == "none") return null;

        var serverName = string.IsNullOrWhiteSpace(p.Sni) ? p.Host : p.Sni;

        if (p.Security == "tls")
        {
            return new SingBoxTls
            {
                Enabled = true,
                ServerName = serverName,
                Alpn = p.Alpn.Count > 0 ? p.Alpn : null,
                Utls = string.IsNullOrWhiteSpace(p.Fingerprint) ? null : new SingBoxUtls { Enabled = true, Fingerprint = p.Fingerprint }
            };
        }

        if (p.Security == "reality")
        {
            return new SingBoxTls
            {
                Enabled = true,
                ServerName = serverName,
                Alpn = p.Alpn.Count > 0 ? p.Alpn : null,
                Utls = string.IsNullOrWhiteSpace(p.Fingerprint) ? null : new SingBoxUtls { Enabled = true, Fingerprint = p.Fingerprint },
                Reality = new SingBoxReality
                {
                    Enabled = true,
                    PublicKey = p.RealityPublicKey!,
                    ShortId = p.RealityShortId!
                }
            };
        }

        throw new ArgumentException("Unsupported security.");
    }

    private static SingBoxTransport? BuildTransport(VpnProfile p)
    {
        return p.Type switch
        {
            "tcp" => null,
            "ws" => new SingBoxTransport
            {
                Type = "ws",
                Path = p.WsPath ?? "/",
                Headers = string.IsNullOrWhiteSpace(p.WsHost) ? null : new Dictionary<string, string> { ["Host"] = p.WsHost! }
            },
            "grpc" => new SingBoxTransport
            {
                Type = "grpc",
                ServiceName = p.GrpcServiceName ?? "default"
            },
            _ => throw new ArgumentException("Unsupported transport type.")
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class SingBoxConfig
{
    public SingBoxLog Log { get; set; } = new();
    public SingBoxDns? Dns { get; set; }
    public List<SingBoxInbound> Inbounds { get; set; } = new();
    public List<SingBoxOutbound> Outbounds { get; set; } = new();
    public SingBoxRoute Route { get; set; } = new();
}

internal sealed class SingBoxLog
{
    public string Level { get; set; } = "info";
}

internal sealed class SingBoxInbound
{
    public string Type { get; set; } = "mixed";
    public string Tag { get; set; } = "mixed-in";

    // mixed listen fields
    public string? Listen { get; set; }
    public int? ListenPort { get; set; }

    // tun fields
    public string? InterfaceName { get; set; }
    public List<string>? Address { get; set; }
    public int? Mtu { get; set; }
    public bool? AutoRoute { get; set; }
    public bool? StrictRoute { get; set; }
    public string? Stack { get; set; }
}

internal sealed class SingBoxOutbound
{
    public string Type { get; set; } = "";
    public string Tag { get; set; } = "";

    // vless fields
    public string? Server { get; set; }
    public int? ServerPort { get; set; }
    public string? Uuid { get; set; }
    public string? Flow { get; set; }

    public string? DomainResolver { get; set; }
    public SingBoxTls? Tls { get; set; }
    public SingBoxTransport? Transport { get; set; }
}

internal sealed class SingBoxTls
{
    public bool Enabled { get; set; } = true;
    public string? ServerName { get; set; }
    public List<string>? Alpn { get; set; }
    public SingBoxUtls? Utls { get; set; }
    public SingBoxReality? Reality { get; set; }
}

internal sealed class SingBoxUtls
{
    public bool Enabled { get; set; } = true;
    public string Fingerprint { get; set; } = "chrome";
}

internal sealed class SingBoxReality
{
    public bool Enabled { get; set; } = true;
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
}

internal sealed class SingBoxTransport
{
    public string Type { get; set; } = "";

    // ws
    public string? Path { get; set; }
    public Dictionary<string, string>? Headers { get; set; }

    // grpc
    public string? ServiceName { get; set; }
}

internal sealed class SingBoxRoute
{
    public string Final { get; set; } = "proxy";
    public bool? AutoDetectInterface { get; set; }
    public string? DefaultDomainResolver { get; set; }
    public List<SingBoxRouteRule>? Rules { get; set; }
    public List<SingBoxRuleSet>? RuleSet { get; set; }
}

internal sealed class SingBoxRouteRule
{
    public List<int>? Port { get; set; }

    public List<string>? Domain { get; set; }

    public string? Protocol { get; set; }

    public int? IpVersion { get; set; }

    public bool? IpIsPrivate { get; set; }

    public List<string>? ProcessPath { get; set; }

    public List<string>? ProcessName { get; set; }

    public List<string>? RuleSet { get; set; }

    public string Action { get; set; } = "route";
    public string? Outbound { get; set; }
}

internal sealed class SingBoxRuleSet
{
    public string Type { get; set; } = "local";
    public string Tag { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Format { get; set; }
}

internal sealed class SingBoxDns
{
    public List<SingBoxDnsServer> Servers { get; set; } = new();
    public List<SingBoxDnsRule>? Rules { get; set; }
    public string? Final { get; set; }
    public bool? ReverseMapping { get; set; }
    public string? Strategy { get; set; }
}

internal sealed class SingBoxDnsRule
{
    public List<string>? Domain { get; set; }
    public List<string>? RuleSet { get; set; }
    public string Server { get; set; } = "";
}

internal sealed class SingBoxDnsServer
{
    public string Type { get; set; } = "https";
    public string Tag { get; set; } = "doh";
    public string? Server { get; set; }
    public int? ServerPort { get; set; }
    public string? Path { get; set; }
    public SingBoxDnsTls? Tls { get; set; }
    public bool? PreferGo { get; set; }
    public string? Detour { get; set; }
}

internal sealed class SingBoxDnsTls
{
    public bool Enabled { get; set; } = true;
    public string? ServerName { get; set; }
}
