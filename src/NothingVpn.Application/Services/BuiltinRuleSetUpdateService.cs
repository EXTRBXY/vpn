using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class BuiltinRuleSetUpdateService(
    ISettingsService settingsService,
    IRuleSetFileService ruleSetFiles,
    ILogPort log) : IBuiltinRuleSetUpdateService
{
    public async Task RefreshDueAsync(CancellationToken cancellationToken = default)
    {
        var state = settingsService.GetState();
        var now = DateTimeOffset.UtcNow;
        if (!BuiltinRuleSetSyncPolicy.IsDue(
                state.BuiltinRuleSetsAutoUpdate,
                state.BuiltinRuleSetsLastAutoUpdateUtc,
                state.BuiltinRuleSetsUpdateIntervalHours,
                now))
            return;

        var targets = state.UserRuleSets
            .Where(x => !string.IsNullOrWhiteSpace(x.BuiltinId))
            .Where(x => x.Enabled || ruleSetFiles.Exists(x))
            .Select(Clone)
            .ToList();

        if (targets.Count == 0)
        {
            settingsService.UpdateState(current => current.BuiltinRuleSetsLastAutoUpdateUtc = now);
            return;
        }

        var updated = 0;
        var unchanged = 0;
        var failed = 0;
        foreach (var rule in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await ruleSetFiles
                    .DownloadBuiltinAsync(rule, useConditionalRequest: ruleSetFiles.Exists(rule), cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Success)
                {
                    failed++;
                    Log(LogLevel.Warn, $"Не удалось обновить «{rule.Name}»: {result.Error ?? "ошибка загрузки."}");
                    continue;
                }

                rule.RemoteEtag = result.NotModified ? result.NewEtag ?? rule.RemoteEtag : result.NewEtag;
                rule.LastDownloadedUtc = now;
                if (result.NotModified) unchanged++;
                else updated++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                Log(LogLevel.Warn, $"Не удалось обновить «{rule.Name}»: {ex.Message}");
            }
        }

        settingsService.UpdateState(current =>
        {
            foreach (var rule in targets)
            {
                var match = current.UserRuleSets.FirstOrDefault(x =>
                    string.Equals(x.BuiltinId, rule.BuiltinId, StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                match.RemoteEtag = rule.RemoteEtag;
                match.LastDownloadedUtc = rule.LastDownloadedUtc;
            }

            current.BuiltinRuleSetsLastAutoUpdateUtc = now;
        });

        Log(LogLevel.Info,
            $"Автообновление встроенных списков: обновлено {updated}, без изменений {unchanged}, с ошибкой {failed}.");
    }

    private void Log(LogLevel level, string message)
    {
        try { log.Append(level, AppLogSources.RuleSets, message); }
        catch { }
    }

    private static UserRuleSetModel Clone(UserRuleSetModel item) => new()
    {
        Tag = item.Tag,
        Name = item.Name,
        FileName = item.FileName,
        Enabled = item.Enabled,
        Action = item.Action,
        BuiltinId = item.BuiltinId,
        RemoteEtag = item.RemoteEtag,
        LastDownloadedUtc = item.LastDownloadedUtc
    };
}
