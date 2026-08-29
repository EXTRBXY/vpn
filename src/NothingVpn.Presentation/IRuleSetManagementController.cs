using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IRuleSetManagementController
{
    RuleSetManagementSnapshot Load(AppStateModel state);
    UserRuleSetModel CreateUserRuleSet(string name, string fileName);
    void Save(AppStateModel state, IEnumerable<UserRuleSetModel> builtin, IEnumerable<UserRuleSetModel> user);
    void MarkBuiltinFilesRemoved(
        AppStateModel state,
        IEnumerable<UserRuleSetModel> builtin,
        IEnumerable<UserRuleSetModel> user,
        IEnumerable<UserRuleSetModel> removed);
    void MarkDownloaded(AppStateModel state, UserRuleSetModel ruleSet, string? remoteEtag);
}
