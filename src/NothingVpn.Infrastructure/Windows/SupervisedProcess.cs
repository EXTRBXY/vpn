using System.Diagnostics;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Windows;

/// <summary>
/// Долгоживущий процесс ядра: Job Object (умирает вместе с владельцем), вывод в журнал, событие выхода.
/// <see cref="Exited"/> срабатывает только при самостоятельном завершении, не после <see cref="Stop"/>.
/// </summary>
internal sealed class SupervisedProcess(string logSource, InMemoryLogStore logStore) : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private ProcessJobScope? _job;

    public event EventHandler? Exited;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public void Start(ProcessStartInfo startInfo)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
                throw new InvalidOperationException($"{logSource} is already running.");

            var p = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            p.Exited += OnExited;

            if (!p.Start())
                throw new InvalidOperationException($"Failed to start {logSource} process.");

            _job?.Dispose();
            _job = ProcessJobScope.TryAttach(p);
            _process = p;

            _ = Task.Run(() => ProcessOutputPump.PumpAsync(p.StandardOutput, $"{logSource}/stdout", logStore));
            _ = Task.Run(() => ProcessOutputPump.PumpAsync(p.StandardError, $"{logSource}/stderr", logStore));
        }
    }

    public void Stop(TimeSpan timeout)
    {
        Process? proc;
        ProcessJobScope? job;
        lock (_gate)
        {
            proc = _process;
            job = _job;
            _process = null;
            _job = null;
        }

        if (proc is null)
            return;

        try
        {
            ChildProcess.Stop(proc, timeout);
        }
        finally
        {
            job?.Dispose();
            try { proc.Dispose(); } catch { }
        }
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(2));

    private void OnExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
            return;

        ProcessJobScope? job;
        lock (_gate)
        {
            if (!ReferenceEquals(_process, exitedProcess))
                return;

            job = _job;
            _job = null;
            _process = null;
        }

        job?.Dispose();
        Exited?.Invoke(this, EventArgs.Empty);
    }
}
