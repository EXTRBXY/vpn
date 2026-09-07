using System.Diagnostics;
using NothingVpn.Infrastructure.Windows;

namespace NothingVpn.Application.Tests;

public sealed class ProcessJobScopeTests
{
    [Fact]
    public void Dispose_TerminatesAttachedProcess()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = "/d /c pause",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        })!;
        try
        {
            using var job = ProcessJobScope.TryAttach(process);
            Assert.NotNull(job);
            Assert.False(process.HasExited);
            job.Dispose();
            Assert.True(process.WaitForExit(5000), "Closing the job must terminate its process.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }
}
