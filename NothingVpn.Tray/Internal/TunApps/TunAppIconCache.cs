namespace NothingVpn.Tray.Internal.TunApps;

internal sealed class TunAppIconCache
{
    private readonly Dictionary<string, int?> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public static ImageList CreateImageList()
    {
        var list = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(16, 16)
        };
        using (var sysBmp = SystemIcons.Application.ToBitmap())
        {
            if (!TryAppendResized(list, sysBmp))
                list.Images.Add(new Bitmap(list.ImageSize.Width, list.ImageSize.Height));
        }

        return list;
    }

    public int GetImageIndex(ImageList imageList, string exePath, int fallbackIndex = 0)
    {
        if (_byPath.TryGetValue(exePath, out var known))
            return known ?? fallbackIndex;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                _byPath[exePath] = null;
                return fallbackIndex;
            }

            using var bmp = icon.ToBitmap();
            if (!TryAppendResized(imageList, bmp))
            {
                _byPath[exePath] = null;
                return fallbackIndex;
            }

            var idx = imageList.Images.Count - 1;
            _byPath[exePath] = idx;
            return idx;
        }
        catch
        {
            _byPath[exePath] = null;
            return fallbackIndex;
        }
    }

    public void QueueImageLoad(ImageList imageList, string exePath, Control host, Action<int> onReady, int fallbackIndex = 0)
    {
        if (_byPath.TryGetValue(exePath, out var known))
        {
            onReady(known ?? fallbackIndex);
            return;
        }

        lock (_inFlight)
        {
            if (_inFlight.Contains(exePath))
                return;
            _inFlight.Add(exePath);
        }

        _ = Task.Run(() =>
        {
            var index = GetImageIndex(imageList, exePath, fallbackIndex);
            try
            {
                if (host.IsDisposed || !host.IsHandleCreated)
                    return;
                host.BeginInvoke((MethodInvoker)(() => onReady(index)));
            }
            catch
            {
                // ignore
            }
            finally
            {
                lock (_inFlight)
                    _inFlight.Remove(exePath);
            }
        });
    }

    private static bool TryAppendResized(ImageList imageList, Image source)
    {
        try
        {
            if (source.Width <= 0 || source.Height <= 0)
                return false;

            using var resized = new Bitmap(source, imageList.ImageSize);
            if (resized.Width <= 0 || resized.Height <= 0)
                return false;

            imageList.Images.Add((Image)resized.Clone());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
