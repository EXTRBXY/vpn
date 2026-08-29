using System.Windows;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;

namespace NothingVpn.Desktop.Wpf;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileViewModel _viewModel;
    private readonly VpnProfile? _existing;

    public ProfileEditorWindow(ProfileViewModel viewModel, VpnProfile? existing)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _existing = existing;
        Heading.Text = existing is null ? "Добавить профиль" : "Изменить профиль";
        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            LinkBox.Text = VlessLinkFormatter.Build(existing);
        }
        else
        {
            TryPrefillFromClipboard();
        }
        Loaded += (_, _) => (string.IsNullOrWhiteSpace(LinkBox.Text) ? LinkBox : NameBox).Focus();
    }

    private void TryPrefillFromClipboard()
    {
        try
        {
            var value = System.Windows.Clipboard.GetText().Trim();
            if (_viewModel.TryParse(value, out var parsed))
            {
                LinkBox.Text = value;
                NameBox.Text = parsed.Name;
            }
        }
        catch { }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var link = LinkBox.Text.Trim();
            if (link.Length == 0)
                throw new InvalidOperationException("Введите VLESS-ссылку.");
            if (!_viewModel.TryParse(link, out _))
                throw new InvalidOperationException("Не удалось распознать VLESS-ссылку.");
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
            _viewModel.Save(_existing, link, name);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
