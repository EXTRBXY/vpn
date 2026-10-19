using System.Windows.Threading;
using Microsoft.Win32;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Следит за светлой/тёмной темой Windows. Событие системы приходит не всегда, поэтому тема ещё и опрашивается
/// раз в секунду.
/// </summary>
internal sealed class SystemThemeWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private bool _isDarkTheme;

    public SystemThemeWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ThemeManager.ApplySystemTheme();
        _isDarkTheme = ThemeManager.IsDarkTheme();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Accessibility)) return;
        _dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        var dark = ThemeManager.IsDarkTheme();
        if (dark == _isDarkTheme) return;
        _isDarkTheme = dark;
        ThemeManager.ApplySystemTheme();
    }
}
