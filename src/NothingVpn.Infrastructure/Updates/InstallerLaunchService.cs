using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Infrastructure.Updates;

public sealed class InstallerLaunchService : IInstallerLaunchService
{
    internal const string SilentUpgradeArguments = "/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART";
    private readonly IUserProcessLauncher _launcher;

    public InstallerLaunchService() : this(new WindowsUserProcessLauncher()) { }
    internal InstallerLaunchService(IUserProcessLauncher launcher) => _launcher = launcher;

    public void EnsureLaunchAllowed() => _launcher.EnsureAvailable();

    public void ScheduleAfterApplicationExits(string installerPath)
    {
        var fullPath = InstallerFilePolicy.ValidateExistingInstaller(installerPath);
        using var parent = Process.GetCurrentProcess();
        Schedule(fullPath, SilentUpgradeArguments, parent.Id, parent.StartTime.ToUniversalTime().Ticks);
    }

    internal void Schedule(string executable, string arguments, int parentId, long parentStartTicks)
    {
        var pipeName = "NothingVpn.Update." + Guid.NewGuid().ToString("N");
        using var worker = _launcher.Start(BuildWorkerStartInfo(executable, arguments, parentId, parentStartTicks, pipeName));
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            if (WindowsUserProcessLauncher.GetPipeServerId(pipe.SafePipeHandle) != worker.Id)
                throw new InvalidOperationException("Не удалось подтвердить процесс обновления.");
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            if (response != "READY")
                throw new InvalidOperationException(response ?? "Процесс обновления не подтвердил готовность.");
            writer.WriteLine("COMMIT");
            if (reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() != "COMMITTED")
                throw new InvalidOperationException("Не удалось передать обновление установщику.");
        }
        catch
        {
            try { if (!worker.HasExited) worker.Kill(); } catch { }
            throw;
        }
    }

    internal static ProcessStartInfo BuildWorkerStartInfo(string executable, string arguments, int parentId, long parentStartTicks, string pipeName)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
            // Private environment transport avoids native command-line length limits and quoting input as code.
            Arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command \"& ([scriptblock]::Create($env:NOTHINGVPN_UPDATE_SCRIPT))\"",
            WorkingDirectory = system,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.Environment.Clear();
        info.Environment["NOTHINGVPN_UPDATE_SCRIPT"] = InstallerUpdateWorker.Script;
        info.Environment["NOTHINGVPN_UPDATE_EXE"] = executable;
        info.Environment["NOTHINGVPN_UPDATE_ARGS"] = arguments;
        info.Environment["NOTHINGVPN_UPDATE_PARENT"] = parentId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["NOTHINGVPN_UPDATE_STARTED"] = parentStartTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["NOTHINGVPN_UPDATE_PIPE"] = pipeName;
        return info;
    }
}
