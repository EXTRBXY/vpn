using System.Diagnostics;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.Windows;

/// <summary>
/// Долгоживущий процесс ядра: Job Object (умирает вместе с владельцем), вывод в журнал, событие выхода.
/// <see cref="Exited"/> срабатывает только при самостоятельном завершении, не после <see cref="Stop"/>.
/// </summary>
internal sealed class SupervisedProcess(string logSource, InMemoryLogStore logStore,
    Action<Process, TimeSpan>? stopProcess = null) : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private ProcessJobScope? _job;
    private bool _stopping;

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
            if (_stopping || _process is { HasExited: false })
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
        Process proc;
        lock (_gate)
        {
            if (_stopping)
                throw new InvalidOperationException($"Остановка {logSource} уже выполняется.");
            if (_process is null)
                return;
            proc = _process;
            _stopping = true;
        }

        var completed = false;
        try
        {
            // WaitForExit может вызывать Exited: ожидание нельзя держать под локом обработчика.
            (stopProcess ?? ChildProcess.Stop)(proc, timeout);
            if (!proc.HasExited)
                throw new InvalidOperationException($"Не удалось остановить {logSource}: процесс продолжает работать.");

            ProcessJobScope? job;
            lock (_gate)
            {
                proc.Exited -= OnExited;
                _process = null;
                job = _job;
                _job = null;
                completed = true;
            }
            job?.Dispose();
            try { proc.Dispose(); } catch { }
        }
        finally
        {
            bool exitedAfterFailure;
            lock (_gate)
            {
                exitedAfterFailure = !completed && proc.HasExited;
                _stopping = false;
            }
            if (exitedAfterFailure)
                OnExited(proc, EventArgs.Empty);
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
            if (_stopping || !ReferenceEquals(_process, exitedProcess))
                return;

            job = _job;
            _job = null;
            _process = null;
        }

        job?.Dispose();
        Exited?.Invoke(this, EventArgs.Empty);
    }
}
