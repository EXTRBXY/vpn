using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NothingVpn.Infrastructure.Updates;
using Xunit.Abstractions;

namespace NothingVpn.Application.Tests;

public sealed class WindowsUpdateHandoffTests(ITestOutputHelper output)
{
    private const string Probe = """
        $i=[Security.Principal.WindowsIdentity]::GetCurrent()
        $p=[Security.Principal.WindowsPrincipal]::new($i)
        $whoami=Join-Path ([Environment]::SystemDirectory) 'whoami.exe'
        $groups=(& $whoami /groups /fo csv /nh | Out-String)
        $data=@{ Sid=$i.User.Value; Administrator=$p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); Session=[Diagnostics.Process]::GetCurrentProcess().SessionId; Medium=($groups -match 'S-1-16-8192') }
        [IO.File]::WriteAllText($env:NOTHINGVPN_PROBE_RESULT, ($data | ConvertTo-Json))
        """;

    [DesktopUpdateFact]
    public void NativeLaunch_UsesSameUserSessionAndMediumPrivileges()
    {
        var directory = NewDirectory();
        var result = Path.Combine(directory, "token.json");
        try
        {
            var info = ProbeInfo(result);
            using var child = new WindowsUserProcessLauncher().Start(info);
            Assert.True(child.WaitForExit(15000));
            Assert.Equal(0, child.ExitCode);
            VerifyToken(result);
        }
        finally { DeleteDirectory(directory); }
    }

    [DesktopUpdateFact]
    public void Handoff_WaitsForActualParentExit_ThenStartsUnelevatedChild()
    {
        var directory = NewDirectory();
        var result = Path.Combine(directory, "after-parent.json");
        using var parent = Process.Start(new ProcessStartInfo
        {
            FileName = PowerShell,
            Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        })!;
        var launcher = new CaptureLauncher(result);
        try
        {
            var service = new InstallerLaunchService(launcher);
            service.EnsureLaunchAllowed();
            service.Schedule(PowerShell, EncodedArguments(Probe), parent.Id, parent.StartTime.ToUniversalTime().Ticks);
            Assert.NotNull(launcher.Worker);
            Assert.False(launcher.Worker!.WaitForExit(4500));
            Assert.False(File.Exists(result));
            parent.Kill();
            Assert.True(parent.WaitForExit(5000));
            Assert.True(launcher.Worker.WaitForExit(15000));
            Assert.Equal(0, launcher.Worker.ExitCode);
            Assert.True(SpinWait.SpinUntil(() => File.Exists(result), TimeSpan.FromSeconds(10)));
            VerifyToken(result);
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(5000); }
            launcher.Dispose();
            DeleteDirectory(directory);
        }
    }

    [DesktopUpdateFact]
    public void Handoff_RejectsWrongParentIdentity_WithoutStartingPayload()
    {
        var directory = NewDirectory();
        var result = Path.Combine(directory, "must-not-exist.json");
        var launcher = new CaptureLauncher(result);
        using var parent = Process.GetCurrentProcess();
        try
        {
            Assert.Throws<InvalidOperationException>(() => new InstallerLaunchService(launcher)
                .Schedule(PowerShell, EncodedArguments(Probe), parent.Id, 1));
            Assert.False(File.Exists(result));
        }
        finally { launcher.Dispose(); DeleteDirectory(directory); }
    }

    private void VerifyToken(string result)
    {
        var current = WindowsUserProcessLauncher.ReadCurrentFacts();
        if (Environment.GetEnvironmentVariable("NOTHINGVPN_EXPECT_ELEVATED") == "1")
            Assert.True(current.Elevated && current.Administrator, "This run must exercise an actually elevated caller.");
        output.WriteLine($"Caller: elevated={current.Elevated}, administrator={current.Administrator}, integrity={current.Integrity}, session={current.SessionId}");
        using var data = JsonDocument.Parse(File.ReadAllText(result));
        Assert.Equal(current.UserSid, data.RootElement.GetProperty("Sid").GetString());
        Assert.Equal(current.SessionId, data.RootElement.GetProperty("Session").GetInt32());
        Assert.False(data.RootElement.GetProperty("Administrator").GetBoolean());
        Assert.True(data.RootElement.GetProperty("Medium").GetBoolean());
        output.WriteLine("Child: same SID/session, not administrator, medium integrity.");
    }

    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string EncodedArguments(string script) => "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    private static ProcessStartInfo ProbeInfo(string result)
    {
        var info = InstallerLaunchService.BuildWorkerStartInfo("unused", "", 1, 1, "unused");
        info.Environment["NOTHINGVPN_UPDATE_SCRIPT"] = Probe;
        info.Environment["NOTHINGVPN_PROBE_RESULT"] = result;
        return info;
    }
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NothingVpn-Update-'%&-тест-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path)) File.Delete(file);
        Directory.Delete(path);
    }
    private sealed class CaptureLauncher(string result) : IUserProcessLauncher, IDisposable
    {
        private readonly WindowsUserProcessLauncher _native = new();
        public Process? Worker;
        public void EnsureAvailable() => _native.EnsureAvailable();
        public Process Start(ProcessStartInfo info)
        {
            info.Environment["NOTHINGVPN_PROBE_RESULT"] = result;
            var process = _native.Start(info);
            Worker = Process.GetProcessById(process.Id);
            _ = Worker.SafeHandle;
            return process;
        }
        public void Dispose()
        {
            if (Worker is null) return;
            if (!Worker.HasExited) { Worker.Kill(); Worker.WaitForExit(5000); }
            Worker.Dispose();
        }
    }
}

// Interactive-desktop tests are explicit: CI runners may have no Explorer/user desktop.
public sealed class DesktopUpdateFactAttribute : FactAttribute
{
    public DesktopUpdateFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NOTHINGVPN_RUN_DESKTOP_TESTS") != "1")
            Skip = "Run build/Test-UpdateHandoff.ps1 on an interactive Windows desktop.";
    }
}
