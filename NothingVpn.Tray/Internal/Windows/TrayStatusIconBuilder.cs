using System.Drawing;
using System.Drawing.Drawing2D;

namespace NothingVpn.Tray.Internal.Windows;

internal static class TrayStatusIconBuilder
{
    private const int SizePx = 32;

    /// <summary>Базовая иконка приложения + индикатор: зелёный — подключено, красный — нет.</summary>
    public static Icon Create(Icon applicationIcon, bool connected)
    {
        using var bmp = new Bitmap(SizePx, SizePx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var src = applicationIcon.ToBitmap())
                g.DrawImage(src, 0, 0, SizePx, SizePx);

            var fill = connected ? Color.FromArgb(240, 46, 180, 70) : Color.FromArgb(240, 220, 55, 55);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
            const int d = 9;
            var x = SizePx - d - 2;
            var y = SizePx - d - 2;
            g.FillEllipse(brush, x, y, d, d);
            g.DrawEllipse(pen, x, y, d, d);
        }

        using var tmp = Icon.FromHandle(bmp.GetHicon());
        return (Icon)tmp.Clone();
    }
}
