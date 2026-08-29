using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record RuleSetManagementSnapshot(
    IReadOnlyList<UserRuleSetModel> Builtin,
    IReadOnlyList<UserRuleSetModel> User);
