using System.Diagnostics;

namespace NothingVpn.Tray.Internal.Updates;

/// <summary>Отложенный запуск Inno Setup после выхода из процесса приложения.</summary>
internal static class InstallerLauncher
{
    /// <summary>Тихая переустановка: без шагов мастера, с окном прогресса установщика.</summary>
    private const string SilentUpgradeArgs = "/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";

    internal static void ScheduleAfterApplicationExits(string installerPath)
    {
        var quotedExe = "\"" + installerPath.Replace("\"", "\"\"") + "\"";
        var cmd = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(cmd))
            cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = "/c timeout /t 2 /nobreak >nul & start \"\" " + quotedExe + " " + SilentUpgradeArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
