using System.Collections.ObjectModel;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class TunAppsSettingsViewModel : SettingsSection
{
    private readonly ITunAppsController _controller;
    private readonly IAppDiscoveryPort _discovery;
    private readonly ISettingsFeedback _feedback;
    private TunAppListItem? _selectedTunApp;

    internal TunAppsSettingsViewModel(
        ITunAppsController controller,
        IAppDiscoveryPort discovery,
        ISettingsFeedback feedback)
    {
        _controller = controller;
        _discovery = discovery;
        _feedback = feedback;
    }

    public ObservableCollection<TunAppListItem> TunApps { get; } = [];

    public TunAppListItem? SelectedTunApp
    {
        get => _selectedTunApp;
        set
        {
            _selectedTunApp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveTunApp));
        }
    }

    public bool CanRemoveTunApp => SelectedTunApp is not null;

    public void AddTunApp(string path)
    {
        if (!_controller.TryNormalize(path, out var normalized))
        {
            _feedback.ShowError("Укажите полный путь к приложению .exe.");
            return;
        }
        if (TunApps.Any(x => string.Equals(x.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            _feedback.ShowMessage("Это приложение уже есть в списке.");
            return;
        }
        var item = TunAppListItem.FromPath(normalized);
        TunApps.Add(item);
        SelectedTunApp = item;
        _feedback.ShowMessage("Приложение добавлено. Сохраните настройки.");
    }

    public async Task<IReadOnlyList<AppCandidate>> FindTunAppsAsync(CancellationToken cancellationToken = default)
    {
        var selectedPaths = TunApps.Select(x => x.Path).ToArray();
        var installed = _discovery.GetInstalledCandidatesAsync(selectedPaths, cancellationToken);
        var running = _discovery.GetRunningCandidatesAsync(selectedPaths, cancellationToken);
        await Task.WhenAll(installed, running);
        return installed.Result
            .Concat(running.Result)
            .GroupBy(x => x.ExePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    public void RemoveSelectedTunApp()
    {
        if (SelectedTunApp is null) return;
        var index = TunApps.IndexOf(SelectedTunApp);
        if (index < 0) return;
        TunApps.RemoveAt(index);
        SelectedTunApp = TunApps.Count == 0 ? null : TunApps[Math.Min(index, TunApps.Count - 1)];
        _feedback.ShowMessage("Приложение удалено из списка. Сохраните настройки.");
    }

    internal void Load(AppStateModel state)
    {
        // Перечисление материализуется до очистки коллекции.
        var items = _controller.Normalize(state.TunAppProcessPaths).Select(TunAppListItem.FromPath).ToArray();
        SelectedTunApp = null;
        TunApps.Clear();
        foreach (var item in items) TunApps.Add(item);
    }

    internal IReadOnlyList<string> NormalizedPaths() =>
        _controller.Normalize(TunApps.Select(x => x.Path)).ToArray();
}
