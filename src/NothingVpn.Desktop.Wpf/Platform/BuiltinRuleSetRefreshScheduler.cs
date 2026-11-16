using System.Windows.Threading;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Автообновление встроенных .srs: через 5 с после старта и затем каждые 15 минут.
/// Фактический due-интервал задаётся в state.json (по умолчанию 24 ч).
/// </summary>
internal sealed class BuiltinRuleSetRefreshScheduler
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IBuiltinRuleSetUpdateService _service;
    private readonly ILogPort _log;
    private readonly DispatcherTimer _timer;
    private int _refreshRunning;

    public BuiltinRuleSetRefreshScheduler(IBuiltinRuleSetUpdateService service, ILogPort log)
    {
        _service = service;
        _log = log;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await RefreshDueAsync();
    }

    public void Start()
    {
        _timer.Start();
        RefreshAfterStartupAsync().Forget();
    }

    public void Stop() => _timer.Stop();

    private async Task RefreshAfterStartupAsync()
    {
        await Task.Delay(StartupDelay);
        await RefreshDueAsync();
    }

    private async Task RefreshDueAsync()
    {
        if (Interlocked.Exchange(ref _refreshRunning, 1) == 1)
            return;
        try
        {
            await _service.RefreshDueAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Append(LogLevel.Warn, AppLogSources.RuleSets,
                "Не удалось обновить встроенные списки по расписанию: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
        }
    }
}
