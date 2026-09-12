namespace NothingVpn.Desktop.Wpf;

public partial class TunAppPickerWindow : System.Windows.Window
{
    private IReadOnlyList<TunAppListItem> _all = [];
    private readonly CancellationTokenSource _loadCancellation = new();
    private bool _closed;
    private bool _loading = true;
    public string? SelectedPath { get; private set; }

    public TunAppPickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => { _closed = true; _loadCancellation.Cancel(); _loadCancellation.Dispose(); };
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var token = _loadCancellation.Token;
        try
        {
            StatusText.Text = "Поиск приложений…";
            var candidates = await ((SettingsViewModel)DataContext).FindTunAppsAsync(token);
            var items = await Task.Run(() => candidates.Select(candidate =>
            {
                token.ThrowIfCancellationRequested();
                return TunAppListItem.FromCandidate(candidate);
            }).ToArray(), token);
            if (_closed) return;
            _all = items;
            _loading = false;
            Refresh();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) { if (!_closed) StatusText.Text = "Не удалось получить приложения: " + ex.Message; }
    }

    private void OnSearch(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        if (AppsList is null || _loading) return;
        var selectedPath = (AppsList.SelectedItem as TunAppListItem)?.Path;
        var query = SearchBox.Text.Trim();
        var items = _all.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        AppsList.ItemsSource = items;
        AppsList.SelectedItem = items.FirstOrDefault(x => x.Path == selectedPath);
        StatusText.Text = items.Length == 0 ? "Приложения не найдены. Можно выбрать файл .exe в настройках." : $"Найдено: {items.Length}";
    }
    private void OnAccept(object sender, System.Windows.RoutedEventArgs e)
    {
        if (AppsList.SelectedItem is not TunAppListItem item) return;
        SelectedPath = item.Path;
        DialogResult = true;
    }
    private void OnCancel(object sender, System.Windows.RoutedEventArgs e) => DialogResult = false;
}
