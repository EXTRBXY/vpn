using System.ComponentModel;
using System.Windows;

namespace NothingVpn.Desktop.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
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
