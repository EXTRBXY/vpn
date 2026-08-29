using System.Diagnostics;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Infrastructure.Updates;

public sealed class InstallerLaunchService : IInstallerLaunchService
{
    private const string SilentUpgradeArguments = "/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";
    private const int DelaySeconds = 4;

    public void ScheduleAfterApplicationExits(string installerPath)
    {
        var fullPath = InstallerFilePolicy.ValidateExistingInstaller(installerPath);
        var quotedExecutable = $"\"{fullPath.Replace("\"", "\"\"")}\"";
        var commandProcessor = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(commandProcessor))
            commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        var started = Process.Start(new ProcessStartInfo
        {
            FileName = commandProcessor,
            Arguments = $"/c timeout /t {DelaySeconds} /nobreak >nul & start \"\" {quotedExecutable} {SilentUpgradeArguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        if (started is null)
            throw new InvalidOperationException("Не удалось запланировать запуск установщика.");
    }
}
