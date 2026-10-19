using System.Security.AccessControl;
using System.Security.Principal;
using NothingVpn.Infrastructure.Installation;
using NothingVpn.Infrastructure.ServiceHost;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Переход со старой per-user установки (шаг 7б): реестр подменён, файлы — во временном каталоге. Главное — каталог
/// данных пользователя и посторонние файлы не трогаются никогда.
/// </summary>
public sealed class LegacyUserInstallMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpnMigrationTests", Guid.NewGuid().ToString("N"));
    private readonly string _legacyDir;
    private readonly string _dataDir;
    private readonly string _programs;
    private readonly string _desktop;
    private readonly string _newExe;

    public LegacyUserInstallMigrationTests()
    {
        var localAppData = Path.Combine(_root, "LocalAppData");
        _legacyDir = Path.Combine(localAppData, "Programs", "NothingVpn");
        _dataDir = Path.Combine(localAppData, "NothingVpn.Tray");
        _programs = Path.Combine(_root, "StartMenu", "Programs");
        _desktop = Path.Combine(_root, "Desktop");
        _newExe = Path.Combine(_root, "ProgramFiles", "NothingVpn", "NothingVpn.Desktop.Wpf.exe");
        foreach (var dir in new[] { _legacyDir, _dataDir, _programs, _desktop, Path.GetDirectoryName(_newExe)! })
            Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public void RemovesLegacyProgramShortcutsAndUninstallEntry_KeepsUserData()
    {
        foreach (var name in new[]
                 {
                     "NothingVpn.Desktop.Wpf.exe", "NothingVpn.Desktop.Wpf.pdb", "NothingVpn.Domain.pdb", "sing-box.exe",
                     "wintun.dll", "unins000.exe", "unins000.dat"
                 })
            File.WriteAllText(Path.Combine(_legacyDir, name), "x");
        File.WriteAllText(Path.Combine(_dataDir, "state.json"), "{}");
        File.WriteAllText(Path.Combine(_dataDir, "profiles.json"), "{}");
        var group = Path.Combine(_programs, "Nothing VPN");
        Directory.CreateDirectory(group);
        File.WriteAllText(Path.Combine(group, "Nothing VPN.lnk"), "x");
        File.WriteAllText(Path.Combine(group, "Uninstall Nothing VPN.lnk"), "x");
        File.WriteAllText(Path.Combine(_desktop, "Nothing VPN.lnk"), "x");
        var registry = new FakeRegistry
        {
            Entry = new LegacyUninstallEntry(_legacyDir, $"\"{Path.Combine(_legacyDir, "unins000.exe")}\"", "Nothing VPN")
        };

        var result = Migrate(registry, enableAutorun: false);

        Assert.True(result.RemovedLegacyInstall);
        Assert.Equal(0, result.LeftoverFiles);
        Assert.False(Directory.Exists(_legacyDir));
        Assert.False(Directory.Exists(group));
        Assert.False(File.Exists(Path.Combine(_desktop, "Nothing VPN.lnk")));
        Assert.True(registry.EntryDeleted);
        Assert.True(File.Exists(Path.Combine(_dataDir, "state.json")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "profiles.json")));
        Assert.Null(registry.Autorun);
    }

    [Fact]
    public void DeletesOnlyKnownFiles_EvenIfInstallLocationPointsElsewhere()
    {
        // Путь в HKCU записывает пользователь: каталог с чужими файлами рекурсивно не удаляется.
        var foreign = Path.Combine(_root, "Apps");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "other.exe"), "x");
        File.WriteAllText(Path.Combine(foreign, "sing-box.exe"), "x");
        var registry = new FakeRegistry { Entry = new LegacyUninstallEntry(foreign + "\\", "\"C:\\Windows\\regedit.exe\"", "..\\..") };

        Migrate(registry, enableAutorun: false);

        Assert.True(File.Exists(Path.Combine(foreign, "other.exe")));
        Assert.False(File.Exists(Path.Combine(foreign, "sing-box.exe")));
        Assert.True(Directory.Exists(foreign));
        Assert.True(Directory.Exists(_programs));
    }

    [Fact]
    public void DoesNotTouchNewInstallDirectory()
    {
        var newDir = Path.GetDirectoryName(_newExe)!;
        File.WriteAllText(_newExe, "x");
        File.WriteAllText(Path.Combine(newDir, "sing-box.exe"), "x");
        var registry = new FakeRegistry { Entry = new LegacyUninstallEntry(newDir + "\\", null, null) };

        Migrate(registry, enableAutorun: false);

        Assert.True(File.Exists(_newExe));
        Assert.True(File.Exists(Path.Combine(newDir, "sing-box.exe")));
        Assert.True(registry.EntryDeleted);
    }

    [Fact]
    public void MovesExistingAutorunToNewExecutable()
    {
        var registry = new FakeRegistry
        {
            Entry = new LegacyUninstallEntry(_legacyDir, null, null),
            Autorun = $"\"{Path.Combine(_legacyDir, "NothingVpn.Desktop.Wpf.exe")}\""
        };

        var result = Migrate(registry, enableAutorun: false);

        Assert.True(result.AutorunUpdated);
        Assert.Equal($"\"{_newExe}\"", registry.Autorun);
    }

    [Fact]
    public void WritesAutorunOnlyWhenRequestedOrPresent()
    {
        var registry = new FakeRegistry();

        Assert.False(Migrate(registry, enableAutorun: false).AutorunUpdated);
        Assert.Null(registry.Autorun);

        Assert.True(Migrate(registry, enableAutorun: true).AutorunUpdated);
        Assert.Equal($"\"{_newExe}\"", registry.Autorun);
        Assert.False(Migrate(registry, enableAutorun: true).AutorunUpdated);
        Assert.False(registry.EntryDeleted);
    }

    [Fact]
    public void ServiceDataDirectory_ReplacesPreCreatedUntrustedDirectory()
    {
        // В %ProgramData% подкаталог может создать любой пользователь до установки службы.
        var path = Path.Combine(_root, "ProgramData", "NothingVpn");
        Directory.CreateDirectory(Path.Combine(path, "configs"));
        File.WriteAllText(Path.Combine(path, "configs", "planted.json"), "{}");
        var checks = 0;

        ServiceDataDirectory.EnsureTrustedDirectory(path, CurrentUserOnly(), _ => checks++ > 0);

        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(Path.Combine(path, "configs", "planted.json")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(path)!, "NothingVpn.untrusted-*"));
        Assert.True(new DirectoryInfo(path).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void ServiceDataDirectory_KeepsTrustedDirectory_AndFailsIfStillUntrusted()
    {
        var path = Path.Combine(_root, "ProgramData", "NothingVpn");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "keep.txt"), "x");

        ServiceDataDirectory.EnsureTrustedDirectory(path, CurrentUserOnly(), _ => true);
        Assert.True(File.Exists(Path.Combine(path, "keep.txt")));

        Assert.Throws<InvalidOperationException>(() =>
            ServiceDataDirectory.EnsureTrustedDirectory(path, CurrentUserOnly(), _ => false));
    }

    private LegacyMigrationResult Migrate(FakeRegistry registry, bool enableAutorun) =>
        new LegacyUserInstallMigration(registry, new UserShellFolders(_programs, _desktop), _newExe).Run(enableAutorun);

    private static DirectorySecurity CurrentUserOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private sealed class FakeRegistry : IUserInstallRegistry
    {
        public LegacyUninstallEntry? Entry { get; init; }
        public bool EntryDeleted { get; private set; }
        public string? Autorun { get; set; }

        public LegacyUninstallEntry? ReadLegacyUninstallEntry() => EntryDeleted ? null : Entry;
        public void DeleteLegacyUninstallEntry() => EntryDeleted = Entry is not null;
        public string? ReadAutorun() => Autorun;
        public void WriteAutorun(string command) => Autorun = command;
    }
}
