using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NothingVpn.Infrastructure.Windows;

public static class TrayStatusIconBuilder
{
    private const int SizePx = 32;

    public static Icon Create(Icon applicationIcon, bool connected)
    {
        using var bitmap = new Bitmap(SizePx, SizePx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var source = applicationIcon.ToBitmap())
                graphics.DrawImage(source, 0, 0, SizePx, SizePx);

            var color = connected
                ? Color.FromArgb(240, 46, 180, 70)
                : Color.FromArgb(240, 220, 55, 55);
            using var brush = new SolidBrush(color);
            using var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
            const int diameter = 9;
            var x = SizePx - diameter - 2;
            var y = SizePx - diameter - 2;
            graphics.FillEllipse(brush, x, y, diameter, diameter);
            graphics.DrawEllipse(border, x, y, diameter, diameter);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
