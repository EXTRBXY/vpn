using System.Diagnostics;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Infrastructure.Updates;

public sealed class InstallerLaunchService : IInstallerLaunchService
{
    internal static readonly string[] SilentUpgradeArguments =
        ["/SILENT", "/SP-", "/SUPPRESSMSGBOXES", "/CLOSEAPPLICATIONS", "/NORESTART"];

    internal const string ElevatedClientMessage =
        "Интерфейс запущен от имени администратора: установщик унаследовал бы эти права. " +
        "Закройте Nothing VPN, запустите его обычным способом и повторите обновление.";

    private const string ClientVariablePrefix = "NOTHINGVPN_";

    private readonly Func<bool> _isElevated;
    private readonly Action<ProcessStartInfo> _start;

    public InstallerLaunchService() : this(() => Environment.IsPrivilegedProcess, StartDetached) { }

    internal InstallerLaunchService(Func<bool> isElevated, Action<ProcessStartInfo> start)
    {
        _isElevated = isElevated;
        _start = start;
    }

    public void EnsureLaunchAllowed()
    {
        if (_isElevated())
            throw new InvalidOperationException(ElevatedClientMessage);
    }

    public string Prepare(string installerPath)
    {
        EnsureLaunchAllowed();
        return InstallerFilePolicy.ValidateExistingInstaller(installerPath);
    }

    public void Launch(string installerPath) => _start(BuildStartInfo(Prepare(installerPath)));

    internal static ProcessStartInfo BuildStartInfo(string installerPath)
    {
        // UseShellExecute=false: без ShellExecute нет автоматического повышения по манифесту, ассоциаций и runas.
        // Установщик per-machine (asInvoker, PrivilegesRequired=admin) запрашивает UAC сам.
        var info = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        foreach (var argument in SilentUpgradeArguments)
            info.ArgumentList.Add(argument);
        // Установщик перезапустит интерфейс от имени пользователя; настройки разработки (NOTHINGVPN_ENGINE)
        // текущего процесса туда переходить не должны.
        foreach (var name in info.Environment.Keys.Where(IsClientVariable).ToArray())
            info.Environment.Remove(name);
        return info;
    }

    private static bool IsClientVariable(string name) =>
        name.StartsWith(ClientVariablePrefix, StringComparison.OrdinalIgnoreCase);

    private static void StartDetached(ProcessStartInfo info)
    {
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Не удалось запустить установщик обновления.");
    }
}
