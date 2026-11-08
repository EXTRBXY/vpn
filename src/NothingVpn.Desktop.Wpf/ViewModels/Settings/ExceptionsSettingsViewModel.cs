using System.Collections.ObjectModel;
using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Desktop.Wpf;

public sealed class ExceptionsSettingsViewModel : SettingsSection
{
    private readonly ISettingsFeedback _feedback;
    private DomainExceptionModel? _selectedEntry;
    private string _newDomain = string.Empty;
    private bool _includeSubdomains = DomainExceptionPolicy.DefaultIncludeSubdomains;

    internal ExceptionsSettingsViewModel(ISettingsFeedback feedback)
    {
        _feedback = feedback;
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(() =>
        {
            if (SelectedEntry is not null) Entries.Remove(SelectedEntry);
            SelectedEntry = null;
        });
    }

    public ObservableCollection<DomainExceptionModel> Entries { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public DomainExceptionModel? SelectedEntry
    {
        get => _selectedEntry;
        set { _selectedEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRemove)); }
    }
    public bool CanRemove => SelectedEntry is not null;
    public string NewDomain
    {
        get => _newDomain;
        set { _newDomain = value; OnPropertyChanged(); }
    }
    public bool IncludeSubdomains
    {
        get => _includeSubdomains;
        set { _includeSubdomains = value; OnPropertyChanged(); }
    }

    private void Add()
    {
        try
        {
            var domain = DomainExceptionPolicy.NormalizeDomain(NewDomain);
            DomainExceptionPolicy.Validate(Entries.Select(x => x.Domain).Append(domain));
            var entry = new DomainExceptionModel { Domain = domain, IncludeSubdomains = IncludeSubdomains };
            Entries.Add(entry);
            SelectedEntry = entry;
            NewDomain = string.Empty;
        }
        catch (ArgumentException ex) { _feedback.ShowError(ex.Message); }
    }

    public void Load(AppStateModel state)
    {
        Entries.Clear();
        foreach (var entry in state.DomainExceptions) Entries.Add(entry.Copy());
        SelectedEntry = null;
    }

    public List<DomainExceptionModel> ToSettings() => Entries.Select(x => x.Copy()).ToList();
}
