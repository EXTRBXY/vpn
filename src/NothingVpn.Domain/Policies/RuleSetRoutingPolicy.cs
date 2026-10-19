using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public sealed record RuleSetRoutingDecision(
    IReadOnlyList<string> DirectRuleSetTags,
    IReadOnlyList<string> DnsDirectRuleSetTags,
    bool RequiresSniff,
    bool RequiresSplitDns);

public static class RuleSetRoutingPolicy
{
    /// <summary>
    /// Вычисляет требования к sniff/split DNS по enabled rule-set и режиму DNS/подключения.
    /// </summary>
    /// <param name="entries">Записи rule-set (учитываются только Enabled).</param>
    /// <param name="dnsMode">DNS-режим.</param>
    /// <param name="isTunMode">true для tun / tun_apps</param>
    public static RuleSetRoutingDecision Evaluate(
        IEnumerable<UserRuleSetEntry> entries,
        DnsMode dnsMode,
        bool isTunMode)
    {
        var directTags = new List<string>();
        var dnsTags = new List<string>();
        var anyEnabled = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!entry.Enabled) continue;
            var tag = (entry.Tag ?? string.Empty).Trim();
            if (tag.Length == 0) continue;
            if (!seen.Add(tag)) continue;

            anyEnabled = true;
            if (entry.Action != RuleSetAction.Direct) continue;

            directTags.Add(tag);
            // geoip rule-sets match addresses, not query names — skip for DNS split.
            if (!tag.StartsWith("geoip-", StringComparison.OrdinalIgnoreCase))
                dnsTags.Add(tag);
        }

        var requiresSniff = isTunMode || anyEnabled;
        var requiresSplitDns = dnsMode == DnsMode.Doh && dnsTags.Count > 0;

        return new RuleSetRoutingDecision(directTags, dnsTags, requiresSniff, requiresSplitDns);
    }
}
