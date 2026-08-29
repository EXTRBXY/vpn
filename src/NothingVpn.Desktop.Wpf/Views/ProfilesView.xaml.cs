using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NothingVpn.Application.Models;

namespace NothingVpn.Desktop.Wpf;

public partial class ProfilesView : System.Windows.Controls.UserControl
{
    private ProfileViewModel? ViewModel => DataContext as ProfileViewModel;

    public ProfilesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProfileViewModel oldVm)
            {
                oldVm.EditRequested -= OnEditRequested;
                oldVm.DeleteRequested -= OnDeleteRequested;
            }
            if (e.NewValue is ProfileViewModel newVm)
            {
                newVm.EditRequested += OnEditRequested;
                newVm.DeleteRequested += OnDeleteRequested;
            }
        };
    }

    private void OnProfileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.EditCommand.CanExecute(null) == true)
            ViewModel.EditCommand.Execute(null);
    }

    private void OnEditRequested(object? sender, VpnProfile? profile)
    {
        if (ViewModel is null) return;
        if (profile?.SubscriptionId is not null)
        {
            System.Windows.MessageBox.Show(
                Window.GetWindow(this),
                "Этот профиль управляется подпиской и изменяется при её обновлении.",
                "Профиль",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        var dialog = new ProfileEditorWindow(ViewModel, profile) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OnDeleteRequested(object? sender, VpnProfile? profile)
    {
        if (ViewModel is null || profile is null) return;
        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            $"Удалить профиль «{profile.Name}»?",
            "Удаление профиля",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            ViewModel.DeleteSelected();
    }
}
