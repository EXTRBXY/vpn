using System.Windows;
using System.Windows.Input;

namespace NothingVpn.Desktop.Wpf;

public partial class SubscriptionsWindow : Window
{
    private SubscriptionViewModel ViewModel => (SubscriptionViewModel)DataContext;
    public SubscriptionsWindow(SubscriptionViewModel viewModel) { InitializeComponent(); DataContext = viewModel; viewModel.Reload(); }
    private void OnAdd(object sender, RoutedEventArgs e) => OpenEditor(null, refreshAfterSave: true);
    private void OnEdit(object sender, RoutedEventArgs e) { if (ViewModel.Selected is { } item) OpenEditor(item.Model, false); }
    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => OnEdit(sender, e);
    private void OpenEditor(NothingVpn.Application.Models.SubscriptionModel? model, bool refreshAfterSave)
    {
        var dialog = new SubscriptionEditorWindow(ViewModel, model) { Owner = this };
        if (dialog.ShowDialog() == true && refreshAfterSave)
            _ = RefreshSavedAsync();
    }
    private async Task RefreshSavedAsync()
    {
        var message = await ViewModel.RefreshSelectedAsync();
        ShowResult(message);
    }
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } item) return;
        if (System.Windows.MessageBox.Show(this, $"Удалить подписку «{item.Name}» и полученные из неё профили?", "Удаление подписки", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            ViewModel.DeleteSelected();
    }
    private async void OnRefreshSelected(object sender, RoutedEventArgs e) => ShowResult(await ViewModel.RefreshSelectedAsync());
    private async void OnRefreshAll(object sender, RoutedEventArgs e) => ShowResult(await ViewModel.RefreshAllAsync());
    private void ShowResult(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            System.Windows.MessageBox.Show(this, message, "Подписки", MessageBoxButton.OK, message.StartsWith("Готово", StringComparison.Ordinal) ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
