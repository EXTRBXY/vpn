using System.Diagnostics;
using NothingVpn.Infrastructure.SingBox;

namespace NothingVpn.Application.Tests;

public sealed class SingBoxRunnerTests
{
    [Fact]
    public async Task RunProcessAsync_DrainsStdoutAndStderrWithoutDeadlock()
    {
        var startInfo = CreateCommand(
            "(for /L %i in (1,1,20000) do @echo stderr-line 1>&2) & echo stdout-line");

        var result = await SingBoxRunner.RunProcessAsync(startInfo, TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("stdout-line", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("stderr-line", result.Stderr, StringComparison.Ordinal);
        Assert.True(result.Stderr.Length > 100_000);
    }

    [Fact]
    public async Task RunProcessAsync_KillsProcessAndThrowsWhenTimeoutExpires()
    {
        var startInfo = CreateCommand("ping 127.0.0.1 -n 10 >nul");
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            SingBoxRunner.RunProcessAsync(startInfo, TimeSpan.FromMilliseconds(200)));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    private static ProcessStartInfo CreateCommand(string command)
    {
        var commandPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        return new ProcessStartInfo
        {
            FileName = commandPath,
            Arguments = $"/d /s /c \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }
}
