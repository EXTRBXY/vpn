using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.RuleSets;

internal sealed record BuiltinRuleSetDefinition(
    string BuiltinId,
    string DisplayName,
    string FileName,
    string RouteTag,
    string DownloadUrl);

internal static class BuiltinGeositeRuleSets
{
    internal const string CatalogBrowserUrl = "https://github.com/SagerNet/sing-geosite/tree/rule-set";

    internal static IReadOnlyList<BuiltinRuleSetDefinition> All { get; } =
    [
        new BuiltinRuleSetDefinition(
            BuiltinId: "sing-geosite:category-ru",
            DisplayName: "Geosite: категория RU",
            FileName: "geosite-category-ru.srs",
            RouteTag: "geosite-category-ru",
            DownloadUrl: "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ru.srs"),
        new BuiltinRuleSetDefinition(
            BuiltinId: "sing-geosite:category-ru-ads",
            DisplayName: "Geosite: категория RU — реклама",
            FileName: "geosite-category-ru@ads.srs",
            RouteTag: "geosite-category-ru-ads",
            DownloadUrl: "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ru%40ads.srs"),
        new BuiltinRuleSetDefinition(
            BuiltinId: "sing-geoip:ru",
            DisplayName: "GeoIP: RU",
            FileName: "geoip-ru.srs",
            RouteTag: "geoip-ru",
            DownloadUrl: "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-ru.srs")
    ];

    internal static BuiltinRuleSetDefinition? FindByBuiltinId(string? builtinId)
    {
        if (string.IsNullOrWhiteSpace(builtinId)) return null;
        foreach (var d in All)
        {
            if (string.Equals(d.BuiltinId, builtinId.Trim(), StringComparison.Ordinal))
                return d;
        }

        return null;
    }

    /// <summary>
    /// Добавляет отсутствующие встроенные наборы перед первым пользовательским импортом.
    /// </summary>
    internal static bool EnsureBuiltinGeositeRuleSets(AppState state)
    {
        var list = state.UserRuleSets ??= new List<UserRuleSet>();
        var changed = false;

        foreach (var def in All)
        {
            if (list.Any(x => string.Equals(x.BuiltinId, def.BuiltinId, StringComparison.Ordinal)))
                continue;

            var insertAt = list.FindIndex(x => string.IsNullOrWhiteSpace(x.BuiltinId));
            if (insertAt < 0)
                insertAt = list.Count;

            list.Insert(insertAt, new UserRuleSet
            {
                BuiltinId = def.BuiltinId,
                Tag = def.RouteTag,
                Name = def.DisplayName,
                FileName = def.FileName,
                Enabled = false,
                Action = string.Equals(def.BuiltinId, "sing-geosite:category-ru-ads", StringComparison.Ordinal)
                    ? "block"
                    : "direct"
            });
            changed = true;
        }

        return changed;
    }
}
