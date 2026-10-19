using System.Collections.ObjectModel;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Встроенные и пользовательские rule-set. Все изменения — в черновике; файлы, помеченные на удаление, удаляются
/// только после успешного сохранения настроек (<see cref="DeletePendingFiles"/>).
/// </summary>
public sealed class RuleSetsSettingsViewModel : SettingsSection
{
    private readonly IRuleSetManagementController _controller;
    private readonly IRuleSetFileService _files;
    private readonly ISettingsFeedback _feedback;
    private readonly List<UserRuleSetModel> _pendingFileDeletes = [];
    private bool _isBusy;
    private UserRuleSetModel? _selectedBuiltinRuleSet;
    private UserRuleSetModel? _selectedUserRuleSet;

    internal RuleSetsSettingsViewModel(
        IRuleSetManagementController controller,
        IRuleSetFileService files,
        ISettingsFeedback feedback)
    {
        _controller = controller;
        _files = files;
        _feedback = feedback;
    }

    /// <summary>Загрузка встроенного списка началась или закончилась; пока она идёт, сохранять нельзя.</summary>
    internal event EventHandler? BusyChanged;

    public ObservableCollection<UserRuleSetModel> BuiltinRuleSets { get; } = [];
    public ObservableCollection<UserRuleSetModel> UserRuleSets { get; } = [];

    public UserRuleSetModel? SelectedBuiltinRuleSet
    {
        get => _selectedBuiltinRuleSet;
        set
        {
            _selectedBuiltinRuleSet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUseBuiltinRuleSet));
        }
    }

    public UserRuleSetModel? SelectedUserRuleSet
    {
        get => _selectedUserRuleSet;
        set
        {
            _selectedUserRuleSet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveUserRuleSet));
        }
    }

    public bool CanEditRuleSets => !_isBusy;
    public bool CanUseBuiltinRuleSet => CanEditRuleSets && SelectedBuiltinRuleSet is not null;
    public bool CanRemoveUserRuleSet => CanEditRuleSets && SelectedUserRuleSet is not null;
    public string RuleSetCatalogUrl => _files.CatalogUrl;

    public void ImportRuleSet(string sourcePath)
    {
        if (!CanEditRuleSets) return;
        var imported = _files.Import(sourcePath);
        UserRuleSets.Add(_controller.CreateUserRuleSet(imported.Name, imported.FileName));
        _feedback.ShowMessage("Список добавлен. Сохраните настройки.");
    }

    public void RemoveSelectedRuleSet()
    {
        if (!CanRemoveUserRuleSet) return;
        var selected = SelectedUserRuleSet!;
        _pendingFileDeletes.Add(Clone(selected));
        UserRuleSets.Remove(selected);
        SelectedUserRuleSet = null;
        _feedback.ShowMessage("Список удалён. Сохраните настройки.");
    }

    public async Task DownloadSelectedBuiltinAsync()
    {
        if (!CanUseBuiltinRuleSet) return;
        await DownloadBuiltinAsync(SelectedBuiltinRuleSet!);
    }

    public async Task SetBuiltinEnabledAsync(UserRuleSetModel ruleSet, bool enabled)
    {
        if (!CanEditRuleSets || !BuiltinRuleSets.Contains(ruleSet)) return;
        SelectedBuiltinRuleSet = ruleSet;
        if (enabled && !_files.Exists(ruleSet))
        {
            await DownloadBuiltinAsync(ruleSet);
            return;
        }
        var updated = Clone(ruleSet);
        updated.Enabled = enabled;
        if (enabled) _pendingFileDeletes.RemoveAll(x => x.FileName == ruleSet.FileName);
        ReplaceBuiltin(ruleSet, updated);
        _feedback.ShowMessage("Сохраните настройки, чтобы применить изменения списков.");
    }

    public void RemoveSelectedBuiltin()
    {
        if (!CanUseBuiltinRuleSet) return;
        var selected = SelectedBuiltinRuleSet!;
        _pendingFileDeletes.Add(Clone(selected));
        var updated = Clone(selected);
        updated.Enabled = false;
        updated.RemoteEtag = null;
        updated.LastDownloadedUtc = null;
        ReplaceBuiltin(selected, updated);
        _feedback.ShowMessage("Список отключён. Файл будет удалён при сохранении.");
    }

    internal void Load(AppStateModel state)
    {
        SelectedBuiltinRuleSet = null;
        SelectedUserRuleSet = null;
        BuiltinRuleSets.Clear();
        UserRuleSets.Clear();
        var rules = _controller.Load(state);
        foreach (var item in rules.Builtin) BuiltinRuleSets.Add(Clone(item));
        foreach (var item in rules.User) UserRuleSets.Add(Clone(item));
    }

    /// <summary>Копии всех записей для сохранения; помеченные на удаление файлы сохраняются выключенными.</summary>
    internal UserRuleSetModel[] BuildForSave()
    {
        var rules = BuiltinRuleSets.Concat(UserRuleSets).Select(Clone).ToArray();
        foreach (var removed in _pendingFileDeletes)
            foreach (var rule in rules.Where(x => x.FileName == removed.FileName))
                rule.Enabled = false;
        return rules;
    }

    /// <summary>
    /// Вызывается после успешного сохранения. Неудалённые файлы остаются в очереди для повторной попытки.
    /// </summary>
    internal IReadOnlyList<string> DeletePendingFiles()
    {
        var errors = new List<string>();
        foreach (var removed in _pendingFileDeletes.ToArray())
        {
            try
            {
                _files.Delete(removed);
                _pendingFileDeletes.Remove(removed);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }
        return errors;
    }

    private async Task DownloadBuiltinAsync(UserRuleSetModel selected)
    {
        SetBusy(true);
        _feedback.ShowError($"Скачивание «{selected.Name}»…");
        try
        {
            var result = await _files.DownloadBuiltinAsync(selected, _files.Exists(selected));
            if (!result.Success)
            {
                _feedback.ShowError($"Не удалось скачать «{selected.Name}»: {result.Error ?? "ошибка загрузки."}");
                return;
            }
            var updated = Clone(selected);
            updated.Enabled = true;
            updated.RemoteEtag = result.NotModified ? result.NewEtag ?? updated.RemoteEtag : result.NewEtag;
            updated.LastDownloadedUtc = DateTimeOffset.UtcNow;
            _pendingFileDeletes.RemoveAll(x => x.FileName == selected.FileName);
            ReplaceBuiltin(selected, updated);
            _feedback.ShowMessage(result.NotModified
                ? "Список уже актуален. Сохраните настройки."
                : "Список обновлён. Сохраните настройки.");
        }
        catch (Exception ex)
        {
            _feedback.ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // Загрузка и переключение заменяют запись копией: в черновике не остаётся ссылок на сохранённые данные.
    private void ReplaceBuiltin(UserRuleSetModel original, UserRuleSetModel updated)
    {
        BuiltinRuleSets[BuiltinRuleSets.IndexOf(original)] = updated;
        SelectedBuiltinRuleSet = updated;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OnPropertiesChanged(nameof(CanEditRuleSets), nameof(CanUseBuiltinRuleSet), nameof(CanRemoveUserRuleSet));
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static UserRuleSetModel Clone(UserRuleSetModel item) => new()
    {
        Tag = item.Tag,
        Name = item.Name,
        FileName = item.FileName,
        Enabled = item.Enabled,
        Action = item.Action,
        BuiltinId = item.BuiltinId,
        RemoteEtag = item.RemoteEtag,
        LastDownloadedUtc = item.LastDownloadedUtc
    };
}
