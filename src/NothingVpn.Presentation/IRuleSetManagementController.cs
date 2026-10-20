using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public interface IRuleSetManagementController
{
    RuleSetManagementSnapshot Load(AppStateModel state);
    UserRuleSetModel CreateUserRuleSet(string name, string fileName);
    void Save(AppStateModel state, IEnumerable<UserRuleSetModel> builtin, IEnumerable<UserRuleSetModel> user);
}
