using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NothingVpn.Infrastructure.Installation;

/// <summary>Запись старой per-user установки (≤ 0.6.8) в <c>HKCU\...\Uninstall</c>.</summary>
internal sealed record LegacyUninstallEntry(string? InstallDirectory, string? UninstallerPath, string? IconGroup);

/// <summary>Реестр текущего пользователя, который трогает миграция. Подменяется в тестах.</summary>
internal interface IUserInstallRegistry
{
    LegacyUninstallEntry? ReadLegacyUninstallEntry();
    void DeleteLegacyUninstallEntry();
    string? ReadAutorun();
    void WriteAutorun(string command);
}

/// <summary>Папки оболочки текущего пользователя: меню «Пуск» (Programs) и рабочий стол.</summary>
internal sealed record UserShellFolders(string Programs, string Desktop);

/// <summary>
/// Переход со старой per-user установки на per-machine (ADR-0001, шаг 7б). Запускает установщик от имени исходного
/// пользователя (<c>ExecAsOriginalUser</c>, <c>NothingVpn.Service.exe --migrate-user-install</c>), поэтому работает
/// с его HKCU и профилем, даже если UAC подтвердил другой администратор, и ничего не делает с правами администратора
/// по путям из пользовательского реестра.
/// <para>
/// Старый деинсталлятор не запускается: в тихом режиме он удаляет каталог данных <c>%LOCALAPPDATA%\NothingVpn.Tray</c>.
/// Вместо него удаляются только известные файлы программы, её ярлыки и запись деинсталляции. Каталог данных не
/// затрагивается никогда. Автозапуск <c>HKCU\...\Run\NothingVpn</c>, если он был, переводится на новый exe.
/// </para>
/// </summary>
public sealed class LegacyUserInstallMigration
{
    public const string Switch = "--migrate-user-install";
    public const string AutorunSwitch = "--autorun";
    public const string AppName = "Nothing VPN";

    private static readonly string[] ProgramFiles =
    [
        "NothingVpn.Desktop.Wpf.exe",
        "NothingVpn.Tray.exe",
        "sing-box.exe",
        "wintun.dll",
        "wpf-smoke-error.txt"
    ];

    private static readonly Regex UninstallerName = new(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IUserInstallRegistry _registry;
    private readonly UserShellFolders _folders;
    private readonly string _clientExecutable;

    internal LegacyUserInstallMigration(IUserInstallRegistry registry, UserShellFolders folders, string clientExecutable)
    {
        _registry = registry;
        _folders = folders;
        _clientExecutable = Path.GetFullPath(clientExecutable);
    }

    /// <summary>Миграция для пользователя, от имени которого запущен процесс; клиент — рядом с exe службы.</summary>
    public static int RunForCurrentUser(bool enableAutorun)
    {
        try
        {
            var migration = new LegacyUserInstallMigration(
                new CurrentUserInstallRegistry(),
                new UserShellFolders(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                Path.Combine(AppContext.BaseDirectory, "NothingVpn.Desktop.Wpf.exe"));
            var result = migration.Run(enableAutorun);
            Console.WriteLine(result.ToString());
            return result.LeftoverFiles == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal LegacyMigrationResult Run(bool enableAutorun)
    {
        var removedLegacy = false;
        var leftover = 0;
        var entry = _registry.ReadLegacyUninstallEntry();
        if (entry is not null)
        {
            leftover = RemoveProgramFiles(entry);
            RemoveShortcuts(entry.IconGroup);
            // Запись удаляется, даже если часть файлов занята: оставшийся в «Программах» старый деинсталлятор при
            // удалении предложил бы стереть данные пользователя.
            _registry.DeleteLegacyUninstallEntry();
            removedLegacy = true;
        }

        var autorunUpdated = false;
        if (enableAutorun || _registry.ReadAutorun() is not null)
        {
            var command = $"\"{_clientExecutable}\"";
            if (!string.Equals(_registry.ReadAutorun(), command, StringComparison.OrdinalIgnoreCase))
            {
                _registry.WriteAutorun(command);
                autorunUpdated = true;
            }
        }

        return new LegacyMigrationResult(removedLegacy, leftover, autorunUpdated);
    }

    private int RemoveProgramFiles(LegacyUninstallEntry entry)
    {
        var directory = NormalizeDirectory(entry.InstallDirectory);
        if (directory is null || !Directory.Exists(directory))
            return 0;

        // Старая копия не может лежать там же, где новая; если так — это не та установка, ничего не трогаем.
        var newDirectory = Path.GetDirectoryName(_clientExecutable);
        if (string.Equals(directory, NormalizeDirectory(newDirectory), StringComparison.OrdinalIgnoreCase))
            return 0;

        var files = ProgramFiles.Select(name => Path.Combine(directory, name)).ToList();
        files.AddRange(Directory.EnumerateFiles(directory, "NothingVpn*.pdb", SearchOption.TopDirectoryOnly));
        if (UninstallerIn(directory, entry.UninstallerPath) is { } uninstaller)
        {
            files.Add(uninstaller);
            files.Add(Path.ChangeExtension(uninstaller, ".dat"));
            files.Add(Path.ChangeExtension(uninstaller, ".msg"));
        }

        var leftover = files.Count(file => !TryDeleteFile(file));
        TryDeleteEmptyDirectory(directory);
        return leftover;
    }

    private void RemoveShortcuts(string? iconGroup)
    {
        var group = IsSimpleName(iconGroup) ? iconGroup!.Trim() : AppName;
        if (!string.IsNullOrWhiteSpace(_folders.Programs))
        {
            var groupDirectory = Path.Combine(_folders.Programs, group);
            TryDeleteFile(Path.Combine(groupDirectory, AppName + ".lnk"));
            TryDeleteFile(Path.Combine(groupDirectory, "Uninstall " + AppName + ".lnk"));
            TryDeleteEmptyDirectory(groupDirectory);
        }

        // Новый установщик кладёт ярлыки в общие папки, поэтому личный ярлык старой копии — только дубль на мёртвый путь.
        if (!string.IsNullOrWhiteSpace(_folders.Desktop))
            TryDeleteFile(Path.Combine(_folders.Desktop, AppName + ".lnk"));
    }

    private static string? UninstallerIn(string directory, string? uninstallerPath)
    {
        var path = uninstallerPath?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return null;

        var name = Path.GetFileName(path);
        if (!UninstallerName.IsMatch(name)
            || !string.Equals(NormalizeDirectory(Path.GetDirectoryName(path)), directory, StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(directory, name);
    }

    private static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(trimmed))
            return null;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
    }

    private static bool IsSimpleName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim() is var trimmed
        && trimmed is not ("." or "..")
        && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record LegacyMigrationResult(bool RemovedLegacyInstall, int LeftoverFiles, bool AutorunUpdated)
{
    public override string ToString() =>
        $"legacy={(RemovedLegacyInstall ? "removed" : "absent")} leftover={LeftoverFiles} autorun={(AutorunUpdated ? "updated" : "unchanged")}";
}

internal sealed class CurrentUserInstallRegistry : IUserInstallRegistry
{
    /// <summary>AppId установщика прежний, поэтому имя ключа старой per-user установки известно.</summary>
    public const string LegacyUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2D610D2-2A69-4D7A-9B06-6C0B4E5F5C87}_is1";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "NothingVpn";

    public LegacyUninstallEntry? ReadLegacyUninstallEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyUninstallKey);
        if (key is null)
            return null;

        return new LegacyUninstallEntry(
            key.GetValue("Inno Setup: App Path") as string ?? key.GetValue("InstallLocation") as string,
            key.GetValue("UninstallString") as string,
            key.GetValue("Inno Setup: Icon Group") as string);
    }

    public void DeleteLegacyUninstallEntry() =>
        Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKey, throwOnMissingSubKey: false);

    public string? ReadAutorun()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) as string;
    }

    public void WriteAutorun(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(RunValue, command, RegistryValueKind.String);
    }
}
