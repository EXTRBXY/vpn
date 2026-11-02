using System.Diagnostics;

namespace NothingVpn.Infrastructure.Windows;

/// <summary>Запуск и остановка дочерних процессов ядра (sing-box, Xray) без окна и с перехватом вывода.</summary>
internal static class ChildProcess
{
    public static ProcessStartInfo CreateStartInfo(string exePath, string arguments) => new()
    {
        FileName = exePath,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8,
        WorkingDirectory = Path.GetDirectoryName(exePath)!
    };

    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await ObserveProcessExitAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new TimeoutException($"{Path.GetFileName(startInfo.FileName)} timed out.");
        }
        catch
        {
            TryKillProcessTree(process);
            await ObserveProcessExitAsync(process).ConfigureAwait(false);
            await ObserveOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>Закрывает потоки, убивает дерево процесса и ждёт выхода; ошибки не выбрасывает.</summary>
    public static void Stop(Process process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited)
                return;

            try { process.StandardOutput?.BaseStream.Close(); } catch { }
            try { process.StandardError?.BaseStream.Close(); } catch { }
            TryKillProcessTree(process);
            process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));
        }
        catch { }
    }

    /// <summary>Последняя строка FATAL/ERROR из вывода проверки конфига, иначе последняя строка.</summary>
    public static string ExtractFailure(string stdout, string stderr)
    {
        static IEnumerable<string> Lines(string text) =>
            (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        string? Pick(IEnumerable<string> lines, string marker) =>
            lines.LastOrDefault(l => l.Contains(marker, StringComparison.OrdinalIgnoreCase));

        var all = Lines(stderr).Concat(Lines(stdout)).ToList();
        return Pick(all, "FATAL")
            ?? Pick(all, "ERROR")
            ?? all.LastOrDefault()?.Trim()
            ?? "";
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static async Task ObserveProcessExitAsync(Process process)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch { }
    }

    private static async Task ObserveOutputAsync(params Task<string>[] outputTasks)
    {
        try { await Task.WhenAll(outputTasks).ConfigureAwait(false); }
        catch { }
    }
}
