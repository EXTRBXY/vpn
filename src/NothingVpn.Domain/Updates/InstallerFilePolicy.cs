namespace NothingVpn.Domain.Updates;

public static class InstallerFilePolicy
{
    public const string ReleaseFileName = "NothingVpnSetup.exe";
    public const string CachedFilePrefix = "NothingVpnSetup-";

    public static bool IsAcceptedFileName(string? path)
    {
        var name = Path.GetFileName(path ?? string.Empty);
        if (name.Length == 0)
            return false;
        return string.Equals(name, ReleaseFileName, StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(CachedFilePrefix, StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public static string ValidateExistingInstaller(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Путь к установщику не задан.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!IsAcceptedFileName(fullPath))
            throw new InvalidOperationException("Некорректный файл установщика.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Файл установщика не найден.", fullPath);
        return fullPath;
    }
}
