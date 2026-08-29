namespace NothingVpn.Domain.Updates;

public static class InstallerDownloadUrlValidator
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };

    public static void EnsureValid(string url, string expectedAssetFileName, bool requireAssetFileName = true)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL установщика пуст.", nameof(url));
        if (string.IsNullOrWhiteSpace(expectedAssetFileName))
            throw new ArgumentException("Ожидаемое имя ассета пусто.", nameof(expectedAssetFileName));

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("Некорректный URL установщика.", nameof(url));

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("URL установщика должен использовать https.", nameof(url));

        if (!AllowedHosts.Contains(uri.Host))
            throw new ArgumentException($"Хост URL установщика не в allowlist: {uri.Host}", nameof(url));

        if (!requireAssetFileName)
            return;

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.Equals(fileName, expectedAssetFileName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Имя файла установщика должно быть «{expectedAssetFileName}».",
                nameof(url));
    }
}
