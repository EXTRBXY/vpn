namespace NothingVpn.Infrastructure.Windows;

/// <summary>Поиск exe ядра. Служба (ADR-0001) берёт файлы только из каталога установки, клиент при разработке — и рядом.</summary>
internal static class ExecutableLocator
{
    public static string? InInstallDirectory(string installDirectory, string fileName)
    {
        var path = Path.Combine(Path.GetFullPath(installDirectory), fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Подсказка, каталог приложения и его bin\, старая per-user установка, затем до пяти родительских каталогов.
    /// Среди найденных побеждает первый, для которого <paramref name="isPreferred"/> истинно.
    /// </summary>
    public static string? Find(string fileName, string? hint, Func<string, bool>? isPreferred = null)
    {
        if (!string.IsNullOrWhiteSpace(hint) && File.Exists(hint))
            return Path.GetFullPath(hint);

        var baseDir = AppContext.BaseDirectory;
        var legacyInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "NothingVpn");

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(hint))
            candidates.Add(Path.Combine(baseDir, hint));
        candidates.Add(Path.Combine(baseDir, fileName));
        candidates.Add(Path.Combine(baseDir, "bin", fileName));
        candidates.Add(Path.Combine(legacyInstallDir, fileName));

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 5 && dir.Parent is not null; i++)
        {
            dir = dir.Parent;
            candidates.Add(Path.Combine(dir.FullName, fileName));
            candidates.Add(Path.Combine(dir.FullName, "bin", fileName));
        }

        string? fallback = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
                continue;

            var full = Path.GetFullPath(candidate);
            if (isPreferred?.Invoke(full) != false)
                return full;

            fallback ??= full;
        }

        return fallback;
    }
}
