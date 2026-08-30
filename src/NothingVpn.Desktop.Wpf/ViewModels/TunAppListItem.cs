using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingVpn.Infrastructure.TunApps;

namespace NothingVpn.Desktop.Wpf;

public sealed class TunAppListItem
{
    private TunAppListItem(string path, string name)
    {
        Path = path;
        Name = name;
        Icon = LoadIcon(path);
    }

    public string Path { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }

    public static TunAppListItem FromPath(string path) => new(path, GetDisplayName(path));

    public static TunAppListItem FromCandidate(AppCandidate candidate) =>
        new(candidate.ExePath, string.IsNullOrWhiteSpace(candidate.DisplayName)
            ? GetDisplayName(candidate.ExePath)
            : candidate.DisplayName);

    private static string GetDisplayName(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            var name = version.FileDescription;
            if (string.IsNullOrWhiteSpace(name)) name = version.ProductName;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch
        {
            // A missing or inaccessible executable can still remain in the saved list.
        }

        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
