using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NothingVpn.Application.Models;
using NothingVpn.Desktop.Wpf;
using Color = System.Windows.Media.Color;
using Border = System.Windows.Controls.Border;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace NothingVpn.Application.Tests;

// Runs inside the existing STA/Application fixture: WPF permits only one Application per process.
internal static class WpfThemeChecks
{
    public static void Check()
    {
        var rows = new[] { new DomainExceptionModel { Domain = "hitmos.fm" }, new DomainExceptionModel { Domain = "example.org" } };
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 36, Margin = new Thickness(0, 8, 0, 12) };
        grid.Columns.Add(new DataGridTextColumn { Header = "Домен", Binding = new System.Windows.Data.Binding("Domain"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Включая поддомены", Binding = new System.Windows.Data.Binding("IncludeSubdomains"), Width = 190 });
        grid.SelectedItem = rows[0];
        var list = new ListBox { ItemsSource = new[] { "Выбранный профиль", "Другой профиль" }, SelectedIndex = 0 };
        var combo = new ComboBox { ItemsSource = new[] { "Напрямую", "Через VPN" }, SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) };
        var field = new TextBox { Text = "Домен или имя профиля", Padding = new Thickness(10) };
        var disabled = new TextBox { Text = "Недоступное поле", IsEnabled = false, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Таблицы и списки — выделение без фокуса", FontSize = 20 });
        content.Children.Add(grid);
        content.Children.Add(list);
        content.Children.Add(combo);
        content.Children.Add(field);
        content.Children.Add(disabled);
        var root = new Border { Padding = new Thickness(24), Child = content };
        root.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        var window = new Window { Content = root, Width = 760, Height = 430, ShowInTaskbar = false };
        var settingsVm = new SettingsViewModelTests.Harness().Create();
        settingsVm.Exceptions.NewDomain = "hitmos.fm";
        settingsVm.Exceptions.AddCommand.Execute(null);
        var settingsView = new SettingsView { DataContext = settingsVm };
        var settingsRoot = new Border { Padding = new Thickness(16), Child = settingsView };
        settingsRoot.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        var settingsWindow = new Window { Content = settingsRoot, ShowInTaskbar = false };
        try
        {
            // Reuse the controls across theme changes to catch stale/static resource references.
            foreach (var dark in new[] { true, false, true })
            {
                ThemeManager.ApplyTheme(dark);
                Layout(root, 760, 430);
                Layout(settingsRoot, 850, 800);
                Save(root, dark ? "theme-dark.png" : "theme-light.png");
                Save(settingsRoot, dark ? "settings-dark.png" : "settings-light.png");
                var exceptionsGrid = Find<DataGrid>(settingsView).Single(x => ReferenceEquals(x.ItemsSource, settingsVm.Exceptions.Entries));
                var domainCell = Find<DataGridCell>(exceptionsGrid).First(x => x.Column is DataGridTextColumn);
                AssertBackground(settingsRoot, domainCell, ColorOf("SelectedBrush"));
                var saveButton = Find<System.Windows.Controls.Button>(settingsView).Single(x => Equals(x.Content, "Сохранить все настройки"));
                var buttonText = Assert.Single(Find<TextBlock>(saveButton));
                Assert.Equal(Colors.White, ((SolidColorBrush)buttonText.Foreground).Color);
                Assert.True(Contrast(((SolidColorBrush)buttonText.Foreground).Color, ((SolidColorBrush)saveButton.Background).Color) >= 4.5);
                var selectedCell = (DataGridCell)Find<DataGridCell>(grid).First(x => x.DataContext == rows[0]);
                AssertBackground(root, selectedCell, ColorOf("SelectedBrush"));
                var selectedItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                AssertBackground(root, selectedItem, ColorOf("SelectedBrush"));
                var normalCell = Find<DataGridCell>(grid).First(x => x.DataContext == rows[1]);
                var normal = Pixel(root, normalCell);
                Assert.True(normal == ColorOf("SurfaceBrush") || normal == ColorOf("SubtleBrush"), $"Unexpected row background {normal}");
                AssertBackground(root, field, ColorOf("SurfaceBrush"));
                Assert.True(Contrast(ColorOf("TextBrush"), Pixel(root, selectedCell)) >= 4.5);
                Assert.True(Contrast(ColorOf("TextBrush"), normal) >= 4.5);
            }
        }
        finally { settingsWindow.Close(); window.Close(); ThemeManager.ApplySystemTheme(); }
    }

    public static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var root = new Border { Child = content };
        root.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        window.Content = root;
        try
        {
            foreach (var dark in new[] { false, true })
            {
                ThemeManager.ApplyTheme(dark);
                foreach (var compact in new[] { false, true })
                {
                    var width = compact ? Math.Max(window.MinWidth, 440) : window.Width;
                    var height = compact ? Math.Max(window.MinHeight, 360) : window.Height;
                    Layout(root, width - 16, height - 40);
                    Save(root, $"{name}-{(dark ? "dark" : "light")}{(compact ? "-compact" : "")}.png");
                    foreach (var button in Find<System.Windows.Controls.Button>(root))
                    {
                        if (button.ActualWidth == 0 || !OnScreen(button, root)) continue;
                        var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
                            $"{name}: button '{button.Content}' is clipped at {width}x{height}: {bounds}");
                    }
                }
            }
        }
        finally
        {
            window.Content = null;
            root.Child = null;
            window.Content = content;
            ThemeManager.ApplySystemTheme();
        }
    }

    private static bool OnScreen(DependencyObject element, FrameworkElement root)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            if (current is ScrollViewer) return false;
            if (current == root) break;
        }
        return true;
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new System.Windows.Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static Color ColorOf(string key) => ((SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
    private static void AssertBackground(FrameworkElement root, FrameworkElement element, Color expected) => Assert.Equal(expected, Pixel(root, element));
    private static Color Pixel(FrameworkElement root, FrameworkElement element)
    {
        var point = element.TransformToAncestor(root).Transform(new System.Windows.Point(element.ActualWidth - 12, element.ActualHeight / 2));
        var bitmap = Render(root);
        var bytes = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static RenderTargetBitmap Render(FrameworkElement root)
    {
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static void Save(FrameworkElement root, string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NothingVpn.sln"))) directory = directory.Parent;
        var output = Path.Combine(directory!.FullName, "artifacts", "ui-previews");
        Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(root)));
        using var stream = File.Create(Path.Combine(output, fileName));
        encoder.Save(stream);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte v) { var c = v / 255d; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
}

