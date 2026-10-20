using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Policies;

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
            Action = RuleSetPolicyValidator.DefaultAction
        };
    }

    public void Save(
        AppStateModel state,
        IEnumerable<UserRuleSetModel> builtin,
        IEnumerable<UserRuleSetModel> user)
    {
        var ruleSets = builtin.Concat(user).ToList();
        _settingsService.UpdateState(current => current.UserRuleSets = ruleSets.ToList());
        state.UserRuleSets = ruleSets;
    }

    private static bool IsBuiltin(UserRuleSetModel ruleSet) =>
        !string.IsNullOrWhiteSpace(ruleSet.BuiltinId);
}
