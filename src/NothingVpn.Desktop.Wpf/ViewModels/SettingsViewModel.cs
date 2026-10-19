using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Страница настроек: черновик по разделам и одно сохранение всех разделов через
/// <see cref="IConnectionSettingsController.Save"/>. Черновик переживает навигацию; ошибка валидации не сохраняет
/// ни один раздел.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, ISettingsFeedback
{
    private readonly IConnectionSettingsController _controller;
    private readonly ISettingsService _settingsService;
    private readonly RelayCommand _saveCommand;
    private string? _message;
    private bool _isConnectionRunning;
    private CancellationTokenSource? _messageCancellation;

    public SettingsViewModel(
        IConnectionSettingsController controller,
        ITunAppsController tunAppsController,
        IRuleSetManagementController ruleSetController,
        IRuleSetFileService ruleSetFiles,
        IAppDiscoveryPort appDiscovery,
        ISettingsService settingsService,
        UpdateViewModel updates)
    {
        _controller = controller;
        _settingsService = settingsService;
        Apps = new TunAppsSettingsViewModel(tunAppsController, appDiscovery, this);
        RuleSets = new RuleSetsSettingsViewModel(ruleSetController, ruleSetFiles, this);
        _saveCommand = new RelayCommand(Save, () => RuleSets.CanEditRuleSets);
        RuleSets.BusyChanged += (_, _) => _saveCommand.RaiseCanExecuteChanged();
        Updates = updates;
        ApplyState(settingsService.GetState());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsChanged;

    public DnsSettingsViewModel Dns { get; } = new();
    public TunSettingsViewModel Tun { get; } = new();
    public TunAppsSettingsViewModel Apps { get; }
    public RuleSetsSettingsViewModel RuleSets { get; }
    public ProxySettingsViewModel Proxy { get; } = new();
    public AppBehaviorSettingsViewModel Behavior { get; } = new();
    public UpdateViewModel Updates { get; }
    public ICommand SaveCommand => _saveCommand;

    public string? Message
    {
        get => _message;
        private set
        {
            _message = value;
            OnPropertyChanged();
        }
    }

    public string ConnectionNotice => _isConnectionRunning
        ? "VPN подключён. Сетевые изменения будут применены после переподключения."
        : string.Empty;

    public void SetConnectionState(bool running)
    {
        if (_isConnectionRunning == running) return;
        _isConnectionRunning = running;
        OnPropertyChanged(nameof(ConnectionNotice));
    }

    /// <summary>Навигация обновляет контекст подключения, не сбрасывая черновик.</summary>
    public void Reload()
    {
        Dns.ApplyConnectionMode(_settingsService.GetState().Mode);
    }

    public void ShowError(string message)
    {
        _messageCancellation?.Cancel();
        Message = message;
    }

    void ISettingsFeedback.ShowMessage(string message) => ShowMessage(message);

    private void Save()
    {
        if (!RuleSets.CanEditRuleSets) return;
        try
        {
            var tun = Tun.ToSettings();
            var paths = Apps.NormalizedPaths();
            var rules = RuleSets.BuildForSave();
            _controller.Save(_settingsService.GetState(), new ConnectionSettingsDraft(Proxy.ToSettings(), tun, Dns.ToSettings())
            {
                TunAppPaths = paths,
                RuleSets = rules,
                LogLevel = Behavior.LogLevel,
                CloseBehavior = Behavior.CloseBehavior
            });
            var deleteErrors = RuleSets.DeletePendingFiles();
            ApplyState(_settingsService.GetState());
            if (deleteErrors.Count > 0)
                ShowError("Настройки сохранены, но не удалось удалить файлы списков: " + string.Join("; ", deleteErrors));
            else
                ShowMessage(_isConnectionRunning
                    ? "Настройки сохранены. Они применятся после переподключения."
                    : "Все настройки сохранены.");
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void ShowMessage(string message)
    {
        _messageCancellation?.Cancel();
        _messageCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _messageCancellation = cancellation;
        Message = message;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            if (ReferenceEquals(_messageCancellation, cancellation))
                Message = null;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyState(AppStateModel state)
    {
        Proxy.Load(state);
        Tun.Load(state);
        Dns.Load(state);
        Behavior.Load(state);
        Apps.Load(state);
        RuleSets.Load(state);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
