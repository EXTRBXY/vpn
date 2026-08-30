using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using NothingVpn.Application.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IConnectionScreenController _screenController;
    private readonly IConnectionController _connectionController;
    private readonly Action _requestExit;
    private readonly AsyncRelayCommand _toggleCommand;
    private AppStateModel _state = new();
    private VpnProfile? _selectedProfile;
    private ModeOption? _selectedMode;
    private ConnectionViewState? _viewState;
    private string? _errorMessage;
    private Page _activePage = Page.Home;

    public MainViewModel(
        IConnectionScreenController screenController,
        IConnectionController connectionController,
        ProfileViewModel profileManager,
        SettingsViewModel settings,
        IConnectionDiagnosticController diagnosticController,
        NothingVpn.Infrastructure.Diagnostics.InMemoryLogStore logStore,
        Action requestExit)
    {
        _screenController = screenController;
        _connectionController = connectionController;
        _requestExit = requestExit;
        ProfileManager = profileManager;
        Settings = settings;
        Settings.SettingsChanged += (_, _) => Load();
        Diagnostics = new DiagnosticsViewModel(diagnosticController, logStore, () => _state.Mode, () => _connectionController.IsRunning);
        Modes =
        [
            new ModeOption("proxy", "Прокси"),
            new ModeOption("tun", "TUN — весь трафик"),
            new ModeOption("tun-apps", "TUN — выбранные приложения")
        ];
        _toggleCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => CanStart || CanStop);
        ToggleConnectionCommand = _toggleCommand;
        ExitCommand = new RelayCommand(_requestExit);
        ShowHomeCommand = new RelayCommand(() => Navigate(Page.Home));
        ShowProfilesCommand = new RelayCommand(() => Navigate(Page.Profiles));
        ShowSettingsCommand = new RelayCommand(() => Navigate(Page.Settings));
        ShowDiagnosticsCommand = new RelayCommand(() => Navigate(Page.Diagnostics));
        ProfileManager.ProfilesChanged += (_, preferredId) => ReloadProfiles(preferredId);
        _connectionController.ConnectionStateChanged += OnConnectionStateChanged;
        Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? ConnectionStateChanged;

    public ObservableCollection<VpnProfile> Profiles { get; } = [];
    public IReadOnlyList<ModeOption> Modes { get; }
    public ICommand ToggleConnectionCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ShowHomeCommand { get; }
    public ICommand ShowProfilesCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowDiagnosticsCommand { get; }
    public ProfileViewModel ProfileManager { get; }
    public SettingsViewModel Settings { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public bool ShowProfiles
    {
        get => _activePage == Page.Profiles;
        set => Navigate(value ? Page.Profiles : Page.Home);
    }
    public bool IsHomeActive => _activePage == Page.Home;
    public bool IsProfilesActive => _activePage == Page.Profiles;
    public bool IsSettingsActive => _activePage == Page.Settings;
    public bool IsDiagnosticsActive => _activePage == Page.Diagnostics;
    public Visibility HomeVisibility => IsHomeActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProfilesVisibility => IsProfilesActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DiagnosticsVisibility => IsDiagnosticsActive ? Visibility.Visible : Visibility.Collapsed;
    private void Navigate(Page page)
    {
        if (_activePage == page) return;
        _activePage = page;
        RaisePage();
    }
    private void RaisePage()
    {
        foreach (var name in new[] { nameof(ShowProfiles), nameof(IsHomeActive), nameof(IsProfilesActive), nameof(IsSettingsActive), nameof(IsDiagnosticsActive), nameof(HomeVisibility), nameof(ProfilesVisibility), nameof(SettingsVisibility), nameof(DiagnosticsVisibility) })
            OnPropertyChanged(name);
    }

    public VpnProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value)) return;
            _selectedProfile = value;
            OnPropertyChanged();
            _screenController.SelectProfile(_state, value?.Id);
            RefreshViewState();
        }
    }

    public ModeOption? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (Equals(_selectedMode, value)) return;
            _selectedMode = value;
            OnPropertyChanged();
            if (value is not null)
            {
                _state.Mode = value.Id;
                _screenController.Save(_state);
            }
            RefreshViewState();
        }
    }

    public bool CanEdit => _viewState?.CanEditConnection ?? false;
    public bool CanStart => _viewState?.CanStart ?? false;
    public bool CanStop => _viewState?.CanStop ?? false;
    public string StatusText => _viewState?.IsRunning == true ? "VPN подключён" : "VPN отключён";
    public string StatusDetail => _viewState?.IsRunning == true
        ? $"{_viewState.ProfileText} · {_viewState.ModeText}"
        : "Ваш трафик сейчас не проходит через VPN";
    public string ConnectionActionText => _viewState?.IsRunning == true ? "Отключить" : "Подключить";
    public MediaBrush StatusBrush => _viewState?.IsRunning == true
        ? new SolidColorBrush(MediaColor.FromRgb(30, 158, 104))
        : new SolidColorBrush(MediaColor.FromRgb(152, 162, 179));
    public MediaBrush ActionBrush => _viewState?.IsRunning == true
        ? new SolidColorBrush(MediaColor.FromRgb(89, 97, 113))
        : new SolidColorBrush(MediaColor.FromRgb(53, 109, 243));
    public string ProfileDetail => _viewState?.ProfileText ?? "—";
    public string ModeDetail => _viewState?.ModeText ?? "—";
    public string DnsDetail => _viewState?.DnsText ?? "—";
    public string RuleSetsDetail => _viewState?.RuleSetsText ?? "—";
    public string? ErrorMessage { get => _errorMessage; private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(ErrorVisibility)); } }
    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public async Task StopForExitAsync()
    {
        if (_connectionController.IsRunning)
            await _connectionController.StopAsync();
    }
    internal async Task ApplyStartupAsync(StartupOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Mode)) SelectedMode=Modes.FirstOrDefault(x=>string.Equals(x.Id,options.Mode,StringComparison.OrdinalIgnoreCase))??SelectedMode;
        if (!string.IsNullOrWhiteSpace(options.ProfileId)) SelectedProfile=Profiles.FirstOrDefault(x=>string.Equals(x.Id,options.ProfileId,StringComparison.OrdinalIgnoreCase))??SelectedProfile;
        if(options.Start&&!_connectionController.IsRunning) await ToggleConnectionAsync();
    }
    public Task ConnectFromTrayAsync()=>_connectionController.IsRunning?Task.CompletedTask:ToggleConnectionAsync();
    public Task DisconnectFromTrayAsync()=>!_connectionController.IsRunning?Task.CompletedTask:ToggleConnectionAsync();

    private void Load()
    {
        var snapshot = _screenController.Load();
        _state = snapshot.State;
        Profiles.Clear();
        foreach (var profile in snapshot.Profiles)
            Profiles.Add(profile);
        _selectedProfile = snapshot.SelectedProfile;
        _selectedMode = Modes.FirstOrDefault(x => string.Equals(x.Id, _state.Mode, StringComparison.OrdinalIgnoreCase)) ?? Modes[0];
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedMode));
        RefreshViewState();
    }

    private void ReloadProfiles(string? preferredId)
    {
        var snapshot = _screenController.Load();
        _state = snapshot.State;
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            _screenController.SelectProfile(_state, preferredId);
            snapshot = _screenController.Load();
            _state = snapshot.State;
        }
        Profiles.Clear();
        foreach (var profile in snapshot.Profiles) Profiles.Add(profile);
        _selectedProfile = snapshot.SelectedProfile;
        OnPropertyChanged(nameof(SelectedProfile));
        RefreshViewState();
    }

    private async Task ToggleConnectionAsync()
    {
        ErrorMessage = null;
        try
        {
            if (_connectionController.IsRunning)
            {
                await _connectionController.StopAsync();
            }
            else if (SelectedProfile is not null && SelectedMode is not null)
            {
                _state.Mode = SelectedMode.Id;
                _screenController.SelectProfile(_state, SelectedProfile.Id);
                var outcome = await _connectionController.StartAsync(SelectedProfile.Id, SelectedMode.Id);
                if (outcome.ExitCurrentProcess)
                    _requestExit();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            RefreshViewState();
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshViewState();
            ConnectionStateChanged?.Invoke(this, connected);
        });
    }

    private void RefreshViewState()
    {
        _viewState = ConnectionViewStateFactory.Create(
            _state,
            SelectedProfile,
            _connectionController.IsRunning,
            _connectionController.IsAdministrator);
        foreach (var name in new[]
        {
            nameof(CanEdit), nameof(CanStart), nameof(CanStop), nameof(StatusText), nameof(StatusDetail),
            nameof(ConnectionActionText), nameof(StatusBrush), nameof(ActionBrush), nameof(ProfileDetail),
            nameof(ModeDetail), nameof(DnsDetail), nameof(RuleSetsDetail)
        }) OnPropertyChanged(name);
        _toggleCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum Page { Home, Profiles, Settings, Diagnostics }
}
