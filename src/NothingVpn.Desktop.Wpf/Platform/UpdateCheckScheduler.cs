using System.Windows.Threading;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Проверка обновлений: при старте, затем каждые 30 минут. Нужна ли проверка (раз в ~23,5 ч), решает
/// <see cref="UpdateViewModel.CheckIfDueAsync"/>.
/// </summary>
internal sealed class UpdateCheckScheduler
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly UpdateViewModel _updates;
    private readonly DispatcherTimer _timer;

    public UpdateCheckScheduler(UpdateViewModel updates)
    {
        _updates = updates;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await _updates.CheckIfDueAsync();
    }

    public void Start()
    {
        _updates.CheckAtStartupAsync().Forget();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
