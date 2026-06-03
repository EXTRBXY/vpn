using System.Text.Json;
using NothingVpn.Domain.Policies;
using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Tray.Internal.SingBox;

internal static class SingBoxConfigGenerator
{
    public static string WriteConfig(AppPaths paths, VlessProfile profile, AppState state)
    {
        var config = Build(paths, profile, state);
        var json = JsonSerializer.Serialize(config, JsonOptions());

        var mode = ConnectionPolicy.NormalizeMode(state.Mode);
        var path = Path.Combine(paths.ConfigsDir, $"{profile.Id}.{mode}.json");
        File.WriteAllText(path, json);
        return path;
    }

    public static SingBoxConfig Build(AppPaths paths, VlessProfile profile, AppState state)
    {
        var mode = ConnectionPolicy.NormalizeMode(state.Mode);
        var useTun = ConnectionPolicy.IsTunMode(mode);
        var inbounds = useTun
            ? new List<SingBoxInbound> { BuildTunInbound(state, profile) }
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

        var useDohResolver = useTun && string.Equals(state.DnsMode, "doh", StringComparison.OrdinalIgnoreCase);
        var hasEnabledUserRuleSets = (state.UserRuleSets ?? []).Any(x => x.Enabled);

        return new SingBoxConfig
        {
            Log = new SingBoxLog { Level = NormalizeLogLevel(state.SingBoxLogLevel) },
            Dns = BuildDns(state, mode, useTun, hasEnabledUserRuleSets, profile),
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

    private static List<SingBoxRouteRule> BuildTunHeadRules(VlessProfile profile)
    {
        var rules = new List<SingBoxRouteRule>
        {
            new() { Action = "sniff" },
            new() { Port = new List<int> { 53 }, Action = "hijack-dns" },
            new() { IpIsPrivate = true, Action = "route", Outbound = "direct" }
        };

        foreach (var domain in TunBootstrapPolicy.CollectEndpointDomains(profile.Host, profile.Sni))
        {
            rules.Add(new SingBoxRouteRule
            {
                Domain = new List<string> { domain },
                Action = "route",
                Outbound = "direct"
            });
        }

        return rules;
    }

    private static SingBoxRoute BuildRoute(AppPaths paths, string mode, AppState state, VlessProfile profile, bool useDohResolver)
    {
        var useTun = ConnectionPolicy.IsTunMode(mode);
        var defaultResolver = TunBootstrapPolicy.ResolveDefaultDomainResolver(useTun, useDohResolver);

        var policy = BuildUserRuleSetPolicy(paths, state);
        var userRuleSets = policy.RuleSets;
        var userRules = policy.Rules;

        if (!useTun)
        {
            return new SingBoxRoute
            {
                Final = "proxy",
                AutoDetectInterface = false,
                RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
                Rules = userRules.Count == 0 ? null : userRules
            };
        }

        var rules = BuildTunHeadRules(profile);

        if (string.Equals(mode, ConnectionPolicy.TunAppsMode, StringComparison.Ordinal))
        {
            var procPaths = NormalizeProcessPaths(state.TunAppProcessPaths);
            if (procPaths.Count == 0)
                throw new ArgumentException("tun_apps requires at least one process path.");

            rules.Add(new SingBoxRouteRule
            {
                ProcessPath = procPaths,
                Action = "route",
                Outbound = "proxy"
            });
        }

        rules.AddRange(userRules);

        if (string.Equals(mode, ConnectionPolicy.TunAppsMode, StringComparison.Ordinal))
        {
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

    private static (List<SingBoxRuleSet> RuleSets, List<SingBoxRouteRule> Rules) BuildUserRuleSetPolicy(AppPaths paths, AppState state)
    {
        var ruleSets = new List<SingBoxRuleSet>();
        var rules = new List<SingBoxRouteRule>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in state.UserRuleSets ?? new List<UserRuleSet>())
        {
            if (!rs.Enabled) continue;
            var tag = (rs.Tag ?? "").Trim();
            if (tag.Length == 0) continue;
            if (!seenTags.Add(tag)) continue;

            var fileName = NormalizeRuleSetFileName(rs.FileName);
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

            var action = (rs.Action ?? "direct").Trim().ToLowerInvariant();
            if (action != "direct" && action != "block")
                throw new ArgumentException("User rule-set action must be direct|block.");
            var outbound = action == "block" ? "block" : "direct";

            rules.Add(new SingBoxRouteRule
            {
                RuleSet = new List<string> { tag },
                Action = "route",
                Outbound = outbound
            });
        }

        return (ruleSets, rules);
    }

    internal static List<string> NormalizeProcessPaths(IEnumerable<string>? paths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths ?? Array.Empty<string>())
        {
            var t = (raw ?? "").Trim();
            if (t.Length == 0) continue;
            set.Add(t);
        }

        return set.ToList();
    }

    private static string NormalizeLogLevel(string? level)
    {
        var l = (level ?? "info").Trim().ToLowerInvariant();
        return l switch
        {
            "trace" => "trace",
            "debug" => "debug",
            "info" => "info",
            "warn" => "warn",
            "warning" => "warn",
            "error" => "error",
            "fatal" => "fatal",
            "panic" => "panic",
            _ => "info"
        };
    }

    private static SingBoxInbound BuildMixedInbound(int localMixedPort) => new()
    {
        Type = "mixed",
        Tag = "mixed-in",
        Listen = "127.0.0.1",
        ListenPort = localMixedPort
    };

    private static SingBoxInbound BuildTunInbound(AppState state, VlessProfile profile)
    {
        var addr = NormalizeTunCidr(state.TunAddressCidr, profile.Id);
        var ifBase = string.IsNullOrWhiteSpace(state.TunInterfaceName) ? "NothingVpn" : state.TunInterfaceName.Trim();
        var ifName = $"{ifBase}-{profile.Id[..Math.Min(6, profile.Id.Length)]}";
        // 9000 MTU is often counterproductive on Windows TUN and can increase latency due to fragmentation/PMTU quirks.
        // Prefer a safe default unless explicitly overridden.
        var configuredMtu = state.TunMtu <= 0 ? 9000 : state.TunMtu;
        var mtu = configuredMtu == 9000 ? 1500 : configuredMtu;
        var stack = NormalizeTunStack(state.TunStack);

        return new SingBoxInbound
        {
            Type = "tun",
            Tag = "tun-in",
            InterfaceName = ifName,
            Address = new List<string> { addr },
            Mtu = mtu,
            AutoRoute = state.TunAutoRoute,
            StrictRoute = state.TunStrictRoute,
            Stack = stack
        };
    }

    private static string NormalizeTunStack(string? stack)
    {
        var s = (stack ?? "mixed").Trim().ToLowerInvariant();
        return s switch
        {
            "system" => "system",
            "mixed" => "mixed",
            "gvisor" => "gvisor",
            _ => "mixed"
        };
    }

    private static string NormalizeTunCidr(string? configured, string profileId)
    {
        var c = (configured ?? "").Trim();
        // Legacy default used in earlier build; treat as auto to avoid collisions.
        if (c.Length == 0 || c.Equals("auto", StringComparison.OrdinalIgnoreCase) || c.Equals("172.19.0.1/30", StringComparison.OrdinalIgnoreCase))
        {
            // Use 198.18.0.0/15 (benchmarking range, unlikely to clash with LAN/VPNs).
            // Derive a stable /30 per profile.
            var n = 0;
            for (var i = 0; i < profileId.Length; i++)
                n = unchecked(n * 31 + profileId[i]);
            n = Math.Abs(n);
            var block = n % 32768; // number of /30 blocks inside /15
            var third = (block >> 6) & 0xFF;
            var fourth = (block & 0x3F) * 4 + 1; // +1 -> host address inside /30
            return $"198.18.{third}.{fourth}/30";
        }

        return c;
    }

    private static SingBoxDns? BuildDns(AppState state, string connectionMode, bool useTun, bool hasEnabledUserRuleSets, VlessProfile profile)
    {
        var mode = (state.DnsMode ?? "system").Trim().ToLowerInvariant();
        if (!useTun && mode != "doh" && !hasEnabledUserRuleSets) return null;

        if (mode == "doh")
        {
            var server = string.IsNullOrWhiteSpace(state.DohServer) ? "1.1.1.1" : state.DohServer.Trim();
            var path = string.IsNullOrWhiteSpace(state.DohPath) ? "/dns-query" : state.DohPath.Trim();
            var sni = string.IsNullOrWhiteSpace(state.DohSni) ? null : state.DohSni.Trim();
            var strictRoute = state.TunStrictRoute;
            var bootstrapDomains = useTun
                ? TunBootstrapPolicy.CollectEndpointDomains(profile.Host, profile.Sni)
                : Array.Empty<string>();

            var servers = new List<SingBoxDnsServer>();
            if (useTun)
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
                Tls = sni is null ? null : new SingBoxDnsTls { Enabled = true, ServerName = sni },
                Detour = useTun
                    ? TunBootstrapPolicy.ResolveSingBoxDohDetour(connectionMode, strictRoute, state.DnsDetour)
                    : null,
            });

            return new SingBoxDns
            {
                Final = "doh",
                ReverseMapping = useTun,
                Strategy = useTun ? "prefer_ipv4" : null,
                Servers = servers,
                Rules = useTun && bootstrapDomains.Count > 0
                    ? bootstrapDomains.Select(d => new SingBoxDnsRule
                    {
                        Domain = new List<string> { d },
                        Server = TunBootstrapPolicy.BootstrapLocalDnsTag
                    }).ToList()
                    : null
            };
        }

        return new SingBoxDns
        {
            Final = TunBootstrapPolicy.LocalDnsTag,
            ReverseMapping = useTun ? true : null,
            Strategy = useTun ? "prefer_ipv4" : null,
            Servers =
            [
                new SingBoxDnsServer
                {
                    Type = "local",
                    Tag = TunBootstrapPolicy.LocalDnsTag,
                    PreferGo = false
                }
            ]
        };
    }

    private static string NormalizeRuleSetFileName(string? fileName)
    {
        var raw = (fileName ?? "").Trim();
        if (raw.Length == 0)
            throw new ArgumentException("User rule-set file name is empty.");

        if (Path.IsPathRooted(raw))
            throw new ArgumentException("User rule-set file name must not be an absolute path.");

        // Reject directory traversal and subdirectories; only a file name is allowed.
        var safe = Path.GetFileName(raw);
        if (!string.Equals(safe, raw, StringComparison.Ordinal))
            throw new ArgumentException("User rule-set file name must not contain directories.");

        if (safe.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("User rule-set file name must not contain '..'.");

        return safe;
    }

    private static SingBoxTls? BuildTls(VlessProfile p)
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

    private static SingBoxTransport? BuildTransport(VlessProfile p)
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

    public bool? IpIsPrivate { get; set; }

    public List<string>? ProcessPath { get; set; }

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
