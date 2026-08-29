using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NothingVpn.Application.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class SubscriptionViewModel : INotifyPropertyChanged
{
    private readonly ISubscriptionManagementController _controller;
    private SubscriptionItemViewModel? _selected;
    private bool _busy;

    public SubscriptionViewModel(ISubscriptionManagementController controller)
    {
        _controller = controller;
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProfilesChanged;
    public ObservableCollection<SubscriptionItemViewModel> Items { get; } = [];
    public SubscriptionItemViewModel? Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); }
    }
    public bool HasSelection => Selected is not null && !Busy;
    public bool Busy
    {
        get => _busy;
        private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanInteract)); }
    }
    public bool CanInteract => !Busy;

    public void Reload(string? preferredId = null)
    {
        var selectedId = preferredId ?? Selected?.Model.Id;
        Items.Clear();
        foreach (var item in _controller.Load()) Items.Add(new SubscriptionItemViewModel(item));
        Selected = Items.FirstOrDefault(x => string.Equals(x.Model.Id, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    public void ReloadAfterExternalRefresh()
    {
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public SubscriptionModel Save(SubscriptionModel? existing, string name, string url, bool enabled)
    {
        var saved = _controller.Save(existing?.Id, name, url, enabled);
        Reload(saved.Id);
        return saved;
    }

    public void DeleteSelected()
    {
        if (Selected is null) return;
        _controller.Delete(Selected.Model.Id);
        Reload();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> RefreshSelectedAsync()
    {
        if (Selected is null) return string.Empty;
        return await RefreshAsync([Selected.Model.Id]);
    }

    public Task<string> RefreshAllAsync() => RefreshAsync(Items.Select(x => x.Model.Id));

    private async Task<string> RefreshAsync(IEnumerable<string> ids)
    {
        Busy = true;
        try
        {
            var results = await _controller.RefreshAllAsync(ids);
            Reload();
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            var failed = results.Where(x => !x.Success).ToList();
            if (failed.Count > 0)
                return failed.Count == 1 ? failed[0].Error ?? "Не удалось обновить подписку." : $"Не удалось обновить подписок: {failed.Count}.";
            var added = results.Sum(x => x.Added);
            var updated = results.Sum(x => x.Updated);
            var removed = results.Sum(x => x.Removed);
            return $"Готово. Добавлено: {added}, обновлено: {updated}, удалено: {removed}.";
        }
        finally { Busy = false; }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
