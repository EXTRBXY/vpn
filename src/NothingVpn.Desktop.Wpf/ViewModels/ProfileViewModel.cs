using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class ProfileViewModel : INotifyPropertyChanged
{
    private readonly IProfileManagementController _controller;
    private readonly RelayCommand _editCommand;
    private readonly RelayCommand _deleteCommand;
    private VpnProfile? _selectedProfile;

    public ProfileViewModel(IProfileManagementController controller)
    {
        _controller = controller;
        AddCommand = new RelayCommand(() => EditRequested?.Invoke(this, null));
        _editCommand = new RelayCommand(RequestEdit, () => SelectedProfile is not null);
        _deleteCommand = new RelayCommand(() => DeleteRequested?.Invoke(this, SelectedProfile), () => SelectedProfile is not null);
        EditCommand = _editCommand;
        DeleteCommand = _deleteCommand;
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<VpnProfile?>? EditRequested;
    public event EventHandler<VpnProfile?>? DeleteRequested;
    public event EventHandler<string?>? ProfilesChanged;

    public ObservableCollection<VpnProfile> Profiles { get; } = [];
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public VpnProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value)) return;
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            _editCommand.RaiseCanExecuteChanged();
            _deleteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedProfile is not null;

    public void Reload(string? preferredId = null)
    {
        var snapshot = _controller.Load();
        var selectionId = preferredId ?? SelectedProfile?.Id ?? snapshot.ActiveProfileId;
        Profiles.Clear();
        foreach (var profile in snapshot.Profiles) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(x =>
            string.Equals(x.Id, selectionId, StringComparison.OrdinalIgnoreCase));
    }

    public VpnProfile Save(VpnProfile? existing, string link, string? name)
    {
        var saved = existing is null
            ? _controller.Add(link, name)
            : _controller.Edit(existing.Id, link, name);
        Reload(saved.Id);
        ProfilesChanged?.Invoke(this, saved.Id);
        return saved;
    }

    public void DeleteSelected()
    {
        if (SelectedProfile is null) return;
        var snapshot = _controller.Delete(SelectedProfile.Id);
        Reload(snapshot.ActiveProfileId);
        ProfilesChanged?.Invoke(this, snapshot.ChangedActiveProfileId);
    }

    public bool TryParse(string link, out VpnProfile profile) => _controller.TryParse(link, out profile);

    private void RequestEdit()
    {
        if (SelectedProfile is not null)
            EditRequested?.Invoke(this, SelectedProfile);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
