using System.Windows.Threading;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Автообновление подписок: через 5 секунд после старта и затем каждые 15 минут. Обновляются только включённые
/// и просроченные подписки (<see cref="ISubscriptionService.RefreshAllDueAsync"/>).
/// </summary>
internal sealed class SubscriptionRefreshScheduler
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly ISubscriptionService _service;
    private readonly SubscriptionViewModel _viewModel;
    private readonly ILogPort _log;
    private readonly DispatcherTimer _timer;
    private int _refreshRunning;

    public SubscriptionRefreshScheduler(ISubscriptionService service, SubscriptionViewModel viewModel, ILogPort log)
    {
        _service = service;
        _viewModel = viewModel;
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
            var results = await _service.RefreshAllDueAsync();
            if (results.Count > 0)
                _viewModel.ReloadAfterExternalRefresh();
        }
        catch (Exception ex)
        {
            // Фоновое обновление не мешает работе: ошибка только записывается, следующая попытка — по таймеру.
            _log.Append(LogLevel.Warn, "app", "Не удалось обновить подписки по расписанию: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshRunning, 0);
        }
    }
}
