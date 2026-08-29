using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IRuleSetFileService
{
    string CatalogUrl { get; }
    bool Exists(UserRuleSetModel ruleSet);
    RuleSetImportResult Import(string sourcePath);
    void Delete(UserRuleSetModel ruleSet);
    Task<RuleSetDownloadResult> DownloadBuiltinAsync(
        UserRuleSetModel ruleSet,
        bool useConditionalRequest,
        CancellationToken cancellationToken = default);
}
