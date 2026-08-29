using System.Windows;
using NothingVpn.Application.Models;

namespace NothingVpn.Desktop.Wpf;

public partial class SubscriptionEditorWindow : Window
{
    private readonly SubscriptionViewModel _viewModel;
    private readonly SubscriptionModel? _existing;
    public SubscriptionEditorWindow(SubscriptionViewModel viewModel, SubscriptionModel? existing)
    {
        InitializeComponent(); _viewModel = viewModel; _existing = existing;
        Heading.Text = existing is null ? "Добавить подписку" : "Изменить подписку";
        if (existing is not null) { NameBox.Text = existing.Name; UrlBox.Text = existing.Url; EnabledBox.IsChecked = existing.Enabled; }
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Save(_existing, NameBox.Text.Trim(), UrlBox.Text.Trim(), EnabledBox.IsChecked == true);
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; ErrorText.Visibility = Visibility.Visible; }
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
