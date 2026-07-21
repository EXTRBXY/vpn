using System.Diagnostics;

namespace NothingVpn.Tray.Internal.Updates;

internal static class InstallerLauncher
{
    private const string SilentUpgradeArgs = "/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";

    private const int DelaySeconds = 4;

    internal static void ScheduleAfterApplicationExits(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
            throw new ArgumentException("Путь к установщику не задан.", nameof(installerPath));

        var fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Файл установщика не найден.", fullPath);

        var quotedExe = "\"" + fullPath.Replace("\"", "\"\"") + "\"";
        var cmd = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(cmd))
            cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        var started = Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = $"/c timeout /t {DelaySeconds} /nobreak >nul & start \"\" {quotedExe} {SilentUpgradeArgs}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (started is null)
            throw new InvalidOperationException("Не удалось запланировать запуск установщика.");
    }
}
