using System.Windows;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public partial class ReleaseChangelogWindow : Window
{
    public ReleaseChangelogWindow(ReleaseChangelog changelog)
    {
        InitializeComponent();
        Title = changelog.Title;
        Heading.Text = changelog.Heading;
        BodyBox.Text = changelog.Body;
        if (changelog.LoadFailed)
        {
            BodyBox.Visibility = Visibility.Collapsed;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
