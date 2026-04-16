using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class RuleSetPolicyValidator
{
    public static void ValidateEnabled(IEnumerable<UserRuleSetEntry> entries, string ruleSetsDir)
    {
        var missing = new List<string>();
        var bad = new List<string>();
        var duplicateTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in entries)
        {
            if (!rs.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rs.FileName) || string.IsNullOrWhiteSpace(rs.Tag))
            {
                bad.Add(string.IsNullOrWhiteSpace(rs.Name) ? "(без имени)" : rs.Name);
                continue;
            }

            var tag = rs.Tag.Trim();
            if (!seenTags.Add(tag))
                duplicateTags.Add(tag);

            var action = (rs.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action is not ("direct" or "block"))
            {
                bad.Add(string.IsNullOrWhiteSpace(rs.Name) ? rs.Tag : rs.Name);
                continue;
            }

            var fileName = rs.FileName.Trim();
            if (!IsSafeRuleSetFileName(fileName) || !fileName.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
            {
                bad.Add(string.IsNullOrWhiteSpace(rs.Name) ? rs.Tag : rs.Name);
                continue;
            }

            var fullPath = Path.Combine(ruleSetsDir, fileName);
            if (!File.Exists(fullPath))
            {
                var name = string.IsNullOrWhiteSpace(rs.Name) ? rs.Tag : rs.Name;
                missing.Add($"{name} -> {rs.FileName}");
            }
        }

        if (bad.Count != 0)
            throw new InvalidOperationException("Некоторые rule-set записи повреждены (нет tag/filename/action или небезопасный filename).");

        if (duplicateTags.Count != 0)
            throw new InvalidOperationException("Найдены дублирующиеся rule-set tag.");

        if (missing.Count != 0)
            throw new InvalidOperationException("Не найдены файлы включённых rule-set (.srs).");
    }

    public static bool IsSafeRuleSetFileName(string fileName)
    {
        var raw = (fileName ?? string.Empty).Trim();
        if (raw.Length == 0) return false;
        if (Path.IsPathRooted(raw)) return false;
        var safe = Path.GetFileName(raw);
        if (!string.Equals(safe, raw, StringComparison.Ordinal)) return false;
        if (safe.Contains("..", StringComparison.Ordinal)) return false;
        return true;
    }
}

