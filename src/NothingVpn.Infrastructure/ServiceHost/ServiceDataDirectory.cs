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
        var info = Directory.CreateDirectory(paths.BaseDir);
        if (applyAcl)
            info.SetAccessControl(CreateSecurity());
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
