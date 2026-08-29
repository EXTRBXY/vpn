using NothingVpn.Application.Models;
using NothingVpn.Application.Services;

namespace NothingVpn.Presentation;

public sealed class RuleSetManagementController : IRuleSetManagementController
{
    private readonly ISettingsService _settingsService;

    public RuleSetManagementController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public RuleSetManagementSnapshot Load(AppStateModel state)
    {
        var all = state.UserRuleSets ?? new List<UserRuleSetModel>();
        return new RuleSetManagementSnapshot(
            all.Where(IsBuiltin).ToList(),
            all.Where(ruleSet => !IsBuiltin(ruleSet)).ToList());
    }

    public UserRuleSetModel CreateUserRuleSet(string name, string fileName)
    {
        var normalizedFileName = Path.GetFileName(fileName?.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName) ||
            !normalizedFileName.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Поддерживаются только файлы .srs.", nameof(fileName));
        }

        var normalizedName = name?.Trim();
        return new UserRuleSetModel
        {
            Tag = $"user-ruleset-{Guid.NewGuid():N}"[..("user-ruleset-".Length + 12)],
            Name = string.IsNullOrWhiteSpace(normalizedName)
                ? Path.GetFileNameWithoutExtension(normalizedFileName)
                : normalizedName,
            FileName = normalizedFileName,
            Enabled = true,
            Action = "direct"
        };
    }

    public void Save(
        AppStateModel state,
        IEnumerable<UserRuleSetModel> builtin,
        IEnumerable<UserRuleSetModel> user)
    {
        state.UserRuleSets = builtin.Concat(user).ToList();
        _settingsService.SaveState(state);
    }

    public void MarkBuiltinFilesRemoved(
        AppStateModel state,
        IEnumerable<UserRuleSetModel> builtin,
        IEnumerable<UserRuleSetModel> user,
        IEnumerable<UserRuleSetModel> removed)
    {
        foreach (var ruleSet in removed.Distinct())
        {
            ruleSet.RemoteEtag = null;
            ruleSet.LastDownloadedUtc = null;
            ruleSet.Enabled = false;
        }
        Save(state, builtin, user);
    }

    public void MarkDownloaded(AppStateModel state, UserRuleSetModel ruleSet, string? remoteEtag)
    {
        if (!string.IsNullOrWhiteSpace(remoteEtag))
            ruleSet.RemoteEtag = remoteEtag.Trim();
        ruleSet.LastDownloadedUtc = DateTimeOffset.UtcNow;
        _settingsService.SaveState(state);
    }

    private static bool IsBuiltin(UserRuleSetModel ruleSet) =>
        !string.IsNullOrWhiteSpace(ruleSet.BuiltinId);
}
