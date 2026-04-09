using System.Text.Json;
using NothingVpn.Tray.Internal.Profile;
using NothingVpn.Tray.Internal.Store;

namespace NothingVpn.Tray.Internal.SingBox;

internal static class SingBoxConfigGenerator
{
    public static string WriteConfig(AppPaths paths, VlessProfile profile, AppState state)
    {
        var config = Build(paths, profile, state);
        var json = JsonSerializer.Serialize(config, JsonOptions());

        var mode = NormalizeMode(state.Mode);
        var path = Path.Combine(paths.ConfigsDir, $"{profile.Id}.{mode}.json");
        File.WriteAllText(path, json);
        return path;
    }

    public static SingBoxConfig Build(AppPaths paths, VlessProfile profile, AppState state)
    {
        var mode = NormalizeMode(state.Mode);
        var useTun = mode is "tun" or "tun_apps";
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

        return new SingBoxConfig
        {
            Log = new SingBoxLog { Level = NormalizeLogLevel(state.SingBoxLogLevel) },
            Dns = BuildDns(state, useTun),
            Inbounds = inbounds,
            Outbounds = new List<SingBoxOutbound>
            {
                outbound,
                new SingBoxOutbound { Type = "direct", Tag = "direct" },
                new SingBoxOutbound { Type = "block", Tag = "block" }
            },
            Route = BuildRoute(paths, mode, state, useDohResolver)
        };
    }

    private static SingBoxRoute BuildRoute(AppPaths paths, string mode, AppState state, bool useDohResolver)
    {
        var useTun = mode is "tun" or "tun_apps";

        var userRuleSets = BuildUserRuleSets(paths, state);
        var userRules = BuildUserRuleSetRules(state);

        if (!useTun)
        {
            return new SingBoxRoute
            {
                Final = "proxy",
                AutoDetectInterface = false,
                DefaultDomainResolver = null,
                RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
                Rules = userRules.Count == 0 ? null : userRules
            };
        }

        if (mode == "tun")
        {
            var tunRules = new List<SingBoxRouteRule>
            {
                new() { Port = new List<int> { 53 }, Action = "hijack-dns" }
            };
            tunRules.AddRange(userRules);

            return new SingBoxRoute
            {
                Final = "proxy",
                AutoDetectInterface = true,
                DefaultDomainResolver = useDohResolver ? "doh" : null,
                RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
                Rules = tunRules
            };
        }

        // tun_apps: default direct; selected processes -> proxy (sing-box route rules).
        var procPaths = NormalizeProcessPaths(state.TunAppProcessPaths);
        if (procPaths.Count == 0)
            throw new ArgumentException("tun_apps requires at least one process path.");

        var rules = new List<SingBoxRouteRule>
        {
            new() { Port = new List<int> { 53 }, Action = "hijack-dns" },
            new()
            {
                ProcessPath = procPaths,
                Action = "route",
                Outbound = "proxy"
            }
        };
        if (userRules.Count != 0)
            rules.InsertRange(1, userRules);

        return new SingBoxRoute
        {
            Final = "direct",
            AutoDetectInterface = true,
            DefaultDomainResolver = useDohResolver ? "doh" : null,
            RuleSet = userRuleSets.Count == 0 ? null : userRuleSets,
            Rules = rules
        };
    }

    private static List<SingBoxRuleSet> BuildUserRuleSets(AppPaths paths, AppState state)
    {
        var result = new List<SingBoxRuleSet>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in state.UserRuleSets ?? new List<UserRuleSet>())
        {
            if (!rs.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rs.Tag)) continue;
            if (string.IsNullOrWhiteSpace(rs.FileName)) continue;
            if (!seen.Add(rs.Tag.Trim())) continue;

            var file = rs.FileName.Trim();
            var fullPath = Path.Combine(paths.RuleSetsDir, file);

            result.Add(new SingBoxRuleSet
            {
                Type = "local",
                Tag = rs.Tag.Trim(),
                Path = fullPath,
                // sing-box 1.13.x требует явный format для route.rule_set
                Format = "binary"
            });
        }

        return result;
    }

    private static List<SingBoxRouteRule> BuildUserRuleSetRules(AppState state)
    {
        var rules = new List<SingBoxRouteRule>();
        foreach (var rs in state.UserRuleSets ?? new List<UserRuleSet>())
        {
            if (!rs.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rs.Tag)) continue;

            var action = (rs.Action ?? "direct").Trim().ToLowerInvariant();
            var outbound = action == "block" ? "block" : "direct";

            rules.Add(new SingBoxRouteRule
            {
                RuleSet = new List<string> { rs.Tag.Trim() },
                Action = "route",
                Outbound = outbound
            });
        }
        return rules;
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
        var s = (stack ?? "system").Trim().ToLowerInvariant();
        return s switch
        {
            "system" => "system",
            "mixed" => "mixed",
            "gvisor" => "gvisor",
            _ => "system"
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

    private static SingBoxDns? BuildDns(AppState state, bool useTun)
    {
        var mode = (state.DnsMode ?? "system").Trim().ToLowerInvariant();
        if (!useTun && mode != "doh") return null;

        if (mode == "doh")
        {
            var server = string.IsNullOrWhiteSpace(state.DohServer) ? "1.1.1.1" : state.DohServer.Trim();
            var path = string.IsNullOrWhiteSpace(state.DohPath) ? "/dns-query" : state.DohPath.Trim();
            var sni = string.IsNullOrWhiteSpace(state.DohSni) ? null : state.DohSni.Trim();

            return new SingBoxDns
            {
                Final = "doh",
                ReverseMapping = useTun,
                Servers = new List<SingBoxDnsServer>
                {
                    new()
                    {
                        Type = "https",
                        Tag = "doh",
                        Server = server,
                        ServerPort = 443,
                        Path = path,
                        Tls = sni is null ? null : new SingBoxDnsTls { Enabled = true, ServerName = sni },
                        // Make DNS routing explicit to avoid bootstrap loops in some setups.
                        Detour = useTun ? DohDetour(state) : null,
                        // No detour => default dialer (direct) for bootstrap.
                    }
                }
            };
        }

        // In TUN we must have DNS module when hijack-dns is enabled.
        // Use the native sing-box local DNS server.
        return new SingBoxDns
        {
            Final = "local",
            ReverseMapping = true,
            Servers = new List<SingBoxDnsServer>
            {
                new()
                {
                    Type = "local",
                    Tag = "local",
                    PreferGo = false
                }
            }
        };
    }

    internal static string NormalizeMode(string? mode)
    {
        var m = (mode ?? "proxy").Trim().ToLowerInvariant();
        return m switch
        {
            "tun" => "tun",
            "tun_apps" => "tun_apps",
            _ => "proxy"
        };
    }

    private static string? DohDetour(AppState state)
    {
        // sing-box: detour to direct is meaningless; omit detour for direct.
        var d = (state.DnsDetour ?? "direct").Trim().ToLowerInvariant();
        return d == "proxy" ? "proxy" : null;
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
    public string? Final { get; set; }
    public bool? ReverseMapping { get; set; }
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
