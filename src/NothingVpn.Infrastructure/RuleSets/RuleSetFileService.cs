using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;

namespace NothingVpn.Infrastructure.RuleSets;

public sealed class RuleSetFileService : IRuleSetFileService
{
    private readonly string _ruleSetsDirectory;

    public RuleSetFileService(IAppPathsPort appPaths)
    {
        _ruleSetsDirectory = appPaths.Get().RuleSetsDir;
    }

    public string CatalogUrl => BuiltinGeositeRuleSets.CatalogBrowserUrl;

    public bool Exists(UserRuleSetModel ruleSet)
    {
        var path = TryResolveRuleSetPath(ruleSet.FileName);
        return path is not null && File.Exists(path);
    }

    public RuleSetImportResult Import(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Файл не найден.", sourcePath);
        if (!sourcePath.EndsWith(".srs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Поддерживаются только файлы .srs.");

        Directory.CreateDirectory(_ruleSetsDirectory);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var fileName = Path.GetFileName(sourcePath);
        var destination = ResolveRuleSetPath(fileName);
        if (File.Exists(destination))
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            fileName = $"{baseName}-{suffix}.srs";
            destination = ResolveRuleSetPath(fileName);
        }

        File.Copy(sourcePath, destination, overwrite: false);
        return new RuleSetImportResult(
            string.IsNullOrWhiteSpace(baseName) ? fileName : baseName,
            fileName);
    }

    public void Delete(UserRuleSetModel ruleSet)
    {
        var path = ResolveRuleSetPath(ruleSet.FileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public async Task<RuleSetDownloadResult> DownloadBuiltinAsync(
        UserRuleSetModel ruleSet,
        bool useConditionalRequest,
        CancellationToken cancellationToken = default)
    {
        var definition = BuiltinGeositeRuleSets.FindByBuiltinId(ruleSet.BuiltinId);
        if (definition is null)
            return new RuleSetDownloadResult(false, false, null, "Неизвестный встроенный rule-set.");

        string destination;
        try
        {
            destination = ResolveRuleSetPath(ruleSet.FileName);
        }
        catch (Exception ex)
        {
            return new RuleSetDownloadResult(false, false, null, ex.Message);
        }
        var result = await RuleSetRemoteDownloader.DownloadAsync(
            definition.DownloadUrl,
            destination,
            useConditionalRequest ? ruleSet.RemoteEtag : null,
            cancellationToken).ConfigureAwait(false);
        return new RuleSetDownloadResult(result.Ok, result.NotModified, result.NewEtag, result.Error);
    }

    private string ResolveRuleSetPath(string? fileName) =>
        TryResolveRuleSetPath(fileName)
        ?? throw new InvalidOperationException("Некорректное имя файла rule-set.");

    private string? TryResolveRuleSetPath(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        if (name.Length == 0 ||
            !name.EndsWith(".srs", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
        {
            return null;
        }
        return Path.Combine(_ruleSetsDirectory, name);
    }
}
