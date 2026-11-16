using System.Diagnostics;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Windows;

namespace NothingVpn.Application.Tests;

public sealed class SupervisedProcessTests
{
    [Fact]
    public void Stop_FailureRetainsRunningProcessAndAllowsRetry()
    {
        var failStop = true;
        using var process = new SupervisedProcess("test", new InMemoryLogStore(), (child, timeout) =>
        {
            if (failStop) throw new TimeoutException("test stop timeout");
            ChildProcess.Stop(child, timeout);
        });
        process.Start(StartInfo());
        try
        {
            Assert.Throws<TimeoutException>(() => process.Stop(TimeSpan.FromSeconds(2)));
            Assert.True(process.IsRunning);
            Assert.Throws<InvalidOperationException>(() => process.Start(StartInfo()));
        }
        finally
        {
            failStop = false;
            process.Stop(TimeSpan.FromSeconds(5));
        }
        Assert.False(process.IsRunning);
    }

    [Fact]
    public void Stop_WaitsForRealChildAndDoesNotReportUnexpectedExit()
    {
        using var process = new SupervisedProcess("test", new InMemoryLogStore());
        var exits = 0;
        process.Exited += (_, _) => Interlocked.Increment(ref exits);
        process.Start(StartInfo());
        Assert.True(process.IsRunning);
        process.Stop(TimeSpan.FromSeconds(5));
        Assert.False(process.IsRunning);
        Assert.Equal(0, Volatile.Read(ref exits));
        process.Stop(TimeSpan.FromSeconds(5));
    }

    private static ProcessStartInfo StartInfo() => ChildProcess.CreateStartInfo(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        "/d /s /c \"ping 127.0.0.1 -n 60 >nul\"");
}
