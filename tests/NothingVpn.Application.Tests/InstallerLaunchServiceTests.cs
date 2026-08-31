using System.Diagnostics;
using NothingVpn.Infrastructure.Updates;

namespace NothingVpn.Application.Tests;

public sealed class InstallerLaunchServiceTests
{
    private static readonly WindowsUserProcessLauncher.TokenFacts Elevated = new("S-1-5-21-1", 1, true, 12288, true);

    [Fact]
    public void ElevatedCaller_CanUseSameUserNormalToken() => WindowsUserProcessLauncher.ValidateTarget(
        Elevated, new(Elevated.UserSid, Elevated.SessionId, false, 8192, false));

    [Theory]
    [InlineData("S-1-5-21-2", 1, false, 8192, false)]
    [InlineData("S-1-5-21-1", 2, false, 8192, false)]
    [InlineData("S-1-5-21-1", 1, true, 8192, false)]
    [InlineData("S-1-5-21-1", 1, false, 12288, false)]
    [InlineData("S-1-5-21-1", 1, false, 8192, true)]
    [InlineData("S-1-5-21-1", 1, false, 4096, false)]
    public void UnsafeOrWrongUserToken_IsRejected(string sid, int session, bool elevated, int integrity, bool administrator) =>
        Assert.Throws<InvalidOperationException>(() => WindowsUserProcessLauncher.ValidateTarget(Elevated, new(sid, session, elevated, integrity, administrator)));

    [Fact]
    public void PreflightFailure_IsNotSwallowed()
    {
        var launcher = new FailedLauncher();
        Assert.Throws<UnauthorizedAccessException>(() => new InstallerLaunchService(launcher).EnsureLaunchAllowed());
        Assert.Equal(0, launcher.Starts);
    }

    [Fact]
    public void SchedulingFailure_HasNoElevatedFallback()
    {
        var launcher = new FailedLauncher();
        Assert.Throws<UnauthorizedAccessException>(() => new InstallerLaunchService(launcher).Schedule("unused.exe", "", 1, 1));
        Assert.Equal(1, launcher.Starts);
    }

    [Theory]
    [InlineData(@"C:\Users\O'Brien\%TEMP% & тест\NothingVpnSetup-1.2.3.exe")]
    [InlineData(@"C:\Temp\$(whoami);`literal`\NothingVpnSetup-1.2.3.exe")]
    public void InstallerPath_IsDataNotCommandText(string path)
    {
        var info = InstallerLaunchService.BuildWorkerStartInfo(path, InstallerLaunchService.SilentUpgradeArguments, 123, 456, "test-pipe");
        Assert.Equal(path, info.Environment["NOTHINGVPN_UPDATE_EXE"]);
        Assert.DoesNotContain(path, info.Arguments);
        Assert.DoesNotContain(path, info.Environment["NOTHINGVPN_UPDATE_SCRIPT"]!);
        Assert.Equal(InstallerLaunchService.SilentUpgradeArguments, info.Environment["NOTHINGVPN_UPDATE_ARGS"]);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);
        Assert.True(Path.IsPathFullyQualified(info.FileName));
        Assert.DoesNotContain("runas", info.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailedLauncher : IUserProcessLauncher
    {
        public int Starts;
        public void EnsureAvailable() => throw new UnauthorizedAccessException("No safe token");
        public Process Start(ProcessStartInfo info) { Starts++; throw new UnauthorizedAccessException("No safe token"); }
    }
}
