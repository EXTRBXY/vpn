using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class RuleSetPolicyValidator
{
    public const RuleSetAction DefaultAction = RuleSetAction.Direct;

    public static readonly SettingValueMap<RuleSetAction> Actions = new(
        DefaultAction,
        new Dictionary<RuleSetAction, string>
        {
            [RuleSetAction.Direct] = "direct",
            [RuleSetAction.Block] = "block"
        });

    /// <summary>
    /// Проверяет включённые записи: tag, имя файла, действие, уникальность тегов. Наличие файлов на диске
    /// проверяет вызывающий код (Application, через порт файлов rule-set).
    /// </summary>
    public static void ValidateEnabled(IEnumerable<UserRuleSetEntry> entries)
    {
        var bad = false;
        var duplicateTags = false;
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rs in entries)
        {
            if (!rs.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rs.FileName) || string.IsNullOrWhiteSpace(rs.Tag))
            {
                bad = true;
                continue;
            }

            if (!seenTags.Add(rs.Tag.Trim()))
                duplicateTags = true;

            var fileName = rs.FileName.Trim();
            if (!Actions.IsDefined(rs.Action)
                || !IsSafeRuleSetFileName(fileName)
                || !fileName.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
            {
                bad = true;
            }
        }

        if (bad)
            throw new InvalidOperationException("Некоторые rule-set записи повреждены (нет tag/filename/action или небезопасный filename).");

        if (duplicateTags)
            throw new InvalidOperationException("Найдены дублирующиеся rule-set tag.");
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
