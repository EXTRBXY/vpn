using System.Windows;
using System.Windows.Input;
using NothingVpn.Application.Models;

namespace NothingVpn.Desktop.Wpf;

public partial class SubscriptionsWindow : Window
{
    public SubscriptionsWindow(SubscriptionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Reload();
    }

    private SubscriptionViewModel ViewModel => (SubscriptionViewModel)DataContext;

    private void OnAdd(object sender, RoutedEventArgs e) => OpenEditor(null, refreshAfterSave: true);

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } item)
            OpenEditor(item.Model, refreshAfterSave: false);
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnEdit(sender, e);

    private void OpenEditor(SubscriptionModel? model, bool refreshAfterSave)
    {
        var dialog = new SubscriptionEditorWindow(ViewModel, model) { Owner = this };
        if (dialog.ShowDialog() == true && refreshAfterSave)
            RefreshSavedAsync().Forget();
    }

    private async Task RefreshSavedAsync()
    {
        var message = await ViewModel.RefreshSelectedAsync();
        ShowResult(message);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } item) return;
        var answer = System.Windows.MessageBox.Show(
            this,
            $"Удалить подписку «{item.Name}» и полученные из неё профили?",
            "Удаление подписки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try { ViewModel.Delete(item.Model.Id); }
        catch (Exception ex) { ShowResult(ex.Message); }
    }

    private async void OnRefreshSelected(object sender, RoutedEventArgs e) => ShowResult(await ViewModel.RefreshSelectedAsync());

    private async void OnRefreshAll(object sender, RoutedEventArgs e) => ShowResult(await ViewModel.RefreshAllAsync());

    private void ShowResult(string message)
    {
        if (!IsVisible || string.IsNullOrWhiteSpace(message)) return;
        var icon = message.StartsWith("Готово", StringComparison.Ordinal)
            ? MessageBoxImage.Information
            : MessageBoxImage.Warning;
        System.Windows.MessageBox.Show(this, message, "Подписки", MessageBoxButton.OK, icon);
    }
}
