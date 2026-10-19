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

    /// <summary>Предел одного .srs, переданного ядру содержимым (служба, ADR-0001). Встроенные наборы — десятки КиБ.</summary>
    public const int MaxContentBytes = 8 * 1024 * 1024;

    /// <summary>Предел всех rule-set одного запроса: держит размер сообщения канала службы ограниченным.</summary>
    public const int MaxTotalContentBytes = 16 * 1024 * 1024;

    /// <summary>Проверяет размеры содержимого rule-set (в байтах) по пределам ядра.</summary>
    public static void EnsureContentWithinLimits(IEnumerable<int> contentSizes)
    {
        long total = 0;
        foreach (var size in contentSizes)
        {
            if (size <= 0)
                throw new InvalidOperationException("Файл rule-set (.srs) пустой.");
            if (size > MaxContentBytes)
                throw new InvalidOperationException(
                    $"Файл rule-set (.srs) слишком большой: больше {MaxContentBytes / (1024 * 1024)} МиБ.");

            total += size;
            if (total > MaxTotalContentBytes)
                throw new InvalidOperationException(
                    $"Rule-set (.srs) вместе занимают больше {MaxTotalContentBytes / (1024 * 1024)} МиБ.");
        }
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
