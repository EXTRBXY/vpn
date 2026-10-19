using System.Runtime.InteropServices;
using System.Windows.Threading;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Последний рубеж для исключений, которые не поймал код интерфейса. Ошибка UI-потока записывается в журнал
/// (маскирует <see cref="ILogPort"/>) и показывается пользователю, приложение продолжает работу. Фатальная ошибка
/// записывается, и VPN пытается остановиться, чтобы не оставить включённым системный прокси; если не успеет,
/// прокси при следующем старте восстановит <c>RecoverStaleRuntimeState</c>.
/// </summary>
internal sealed class GlobalExceptionHandler
{
    private const string LogSource = AppLogSources.App;
    private static readonly TimeSpan DefaultEmergencyStopTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogPort _log;
    private readonly Action<string> _showError;
    private readonly Func<Task> _emergencyStop;
    private readonly TimeSpan _emergencyStopTimeout;
    private int _showingError;
    private int _emergencyStopStarted;

    public GlobalExceptionHandler(
        ILogPort log,
        Action<string> showError,
        Func<Task> emergencyStop,
        TimeSpan? emergencyStopTimeout = null)
    {
        _log = log;
        _showError = showError;
        _emergencyStop = emergencyStop;
        _emergencyStopTimeout = emergencyStopTimeout ?? DefaultEmergencyStopTimeout;
    }

    public void Attach(System.Windows.Application application)
    {
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Ошибка UI-потока. Возвращает true, если приложение может продолжить работу.</summary>
    public bool HandleUiException(Exception exception)
    {
        if (IsUnrecoverable(exception))
        {
            HandleFatal(exception);
            return false;
        }

        Report(exception, "Необработанная ошибка интерфейса");
        // Ошибка внутри показа сообщения (или пока оно открыто) только записывается: окна не должны множиться.
        if (Interlocked.Exchange(ref _showingError, 1) == 1)
            return true;

        try
        {
            _showError(
                $"Произошла непредвиденная ошибка: {exception.Message}\n\n" +
                "Приложение продолжит работу. Подробности записаны в журнал на странице «Диагностика».");
        }
        catch (Exception showFailure)
        {
            Report(showFailure, "Не удалось показать сообщение об ошибке");
        }
        finally
        {
            Volatile.Write(ref _showingError, 0);
        }
        return true;
    }

    /// <summary>Процесс завершается. Записать ошибку и попытаться остановить VPN (один раз, с ограничением по времени).</summary>
    public void HandleFatal(Exception? exception)
    {
        if (exception is not null)
            Report(exception, "Критическая ошибка, приложение будет закрыто", LogLevel.Fatal);
        else
            TryAppend(LogLevel.Fatal, "Критическая ошибка, приложение будет закрыто.");

        if (Interlocked.Exchange(ref _emergencyStopStarted, 1) == 1)
            return;

        try
        {
            // Через пул: остановка не должна зависеть от занятого UI-потока.
            if (!Task.Run(_emergencyStop).Wait(_emergencyStopTimeout))
                TryAppend(LogLevel.Warn, "VPN не успел остановиться до закрытия приложения.");
        }
        catch (Exception stopFailure)
        {
            Report(stopFailure, "Не удалось остановить VPN при аварийном завершении");
        }
    }

    public void HandleUnobservedTask(Exception exception) =>
        Report(exception, "Необработанная ошибка фоновой задачи");

    /// <summary>Записать ошибку в журнал без сообщения пользователю.</summary>
    public void Report(Exception exception, string context, LogLevel level = LogLevel.Error) =>
        TryAppend(level, $"{context}: {exception}");

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = HandleUiException(e.Exception);

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        if (e.IsTerminating)
            HandleFatal(exception);
        else if (exception is not null)
            Report(exception, "Необработанная ошибка");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnobservedTask(e.Exception);
        e.SetObserved();
    }

    private void TryAppend(LogLevel level, string message)
    {
        try { _log.Append(level, LogSource, message); }
        catch { }
    }

    // После таких ошибок состояние процесса не гарантировано, продолжать работу небезопасно.
    private static bool IsUnrecoverable(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException or SEHException or InvalidProgramException;
}
