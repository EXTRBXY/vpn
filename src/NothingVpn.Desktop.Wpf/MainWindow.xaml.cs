using System.ComponentModel;
using System.Windows;

namespace NothingVpn.Desktop.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                Hide();
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.IsExitRequested)
            return;
        if (!_viewModel.CloseToTray)
        {
            e.Cancel = true;
            _viewModel.RequestExit();
            return;
        }
        e.Cancel = true;
        Hide();
    }

    public void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
