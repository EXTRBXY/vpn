using System.Security.AccessControl;
using System.Security.Principal;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Infrastructure.ServiceHost;

/// <summary>
/// Каталог данных службы <c>%ProgramData%\NothingVpn</c>: временные конфиги sing-box (<c>configs\</c>) и rule-set
/// сессии (<c>rulesets\</c>). ACL — только SYSTEM и администраторы, без наследования: конфиг лежит открытым текстом
/// и содержит UUID профиля, а пользователь не должен подменять файлы, которые читает sing-box от SYSTEM.
/// </summary>
internal static class ServiceDataDirectory
{
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NothingVpn");

    public static AppPaths Create(string root, bool applyAcl)
    {
        var paths = PathsFor(root);
        if (applyAcl)
            EnsureTrustedDirectory(paths.BaseDir, CreateSecurity(), IsOwnedBySystemOrAdministrators);
        else
            Directory.CreateDirectory(paths.BaseDir);
        Directory.CreateDirectory(paths.ConfigsDir);
        Directory.CreateDirectory(paths.RuleSetsDir);
        Directory.CreateDirectory(paths.LogsDir);
        return paths;
    }

    /// <summary>Пути службы. Файлов состояния и профилей у службы нет: они остаются у пользователя.</summary>
    public static AppPaths PathsFor(string root)
    {
        var baseDir = Path.GetFullPath(root);
        return new AppPaths(
            BaseDir: baseDir,
            ConfigsDir: Path.Combine(baseDir, "configs"),
            RuleSetsDir: Path.Combine(baseDir, "rulesets"),
            LogsDir: Path.Combine(baseDir, "logs"),
            ProfilesJsonPath: string.Empty,
            SubscriptionsJsonPath: string.Empty,
            StateJsonPath: string.Empty);
    }

    /// <summary>
    /// В <c>%ProgramData%</c> любой пользователь может создать подкаталог раньше службы. Такой каталог (или ссылка на
    /// чужой каталог) остаётся под контролем пользователя даже после смены ACL: владелец может вернуть себе доступ и
    /// подменить конфиг, который sing-box читает от SYSTEM. Поэтому каталог с чужим владельцем или точка повторной
    /// обработки откладывается в сторону, и создаётся новый — сразу с защищённым ACL, без окна с унаследованными
    /// правами. Каталог, которым владеют SYSTEM или администраторы, пользователи изменить не могут.
    /// </summary>
    internal static void EnsureTrustedDirectory(string path, DirectorySecurity security, Func<DirectoryInfo, bool> isTrusted)
    {
        if (File.Exists(path))
            File.Delete(path);

        var info = new DirectoryInfo(path);
        if (info.Exists && !isTrusted(info))
        {
            var aside = path + ".untrusted-" + Guid.NewGuid().ToString("N");
            // Перемещение переименовывает саму ссылку, а не каталог, на который она указывает.
            Directory.Move(path, aside);
            try
            {
                var moved = new DirectoryInfo(aside);
                if (moved.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    moved.Delete();
                else
                    moved.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            info.Refresh();
        }

        if (info.Exists)
            info.SetAccessControl(security);
        else
            info.Create(security);

        info.Refresh();
        if (!isTrusted(info))
            throw new InvalidOperationException($"Каталог данных службы {path} не принадлежит SYSTEM или администраторам.");
    }

    private static bool IsOwnedBySystemOrAdministrators(DirectoryInfo directory)
    {
        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;

        var owner = directory.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        return owner is SecurityIdentifier sid
            && (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
    }

    public static DirectorySecurity CreateSecurity()
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                 })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                inherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }
}
