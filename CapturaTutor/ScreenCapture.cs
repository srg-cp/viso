using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace CapturaTutor;

public sealed record Capture(byte[] Png, BitmapSource Preview, Rectangle Bounds);
public static class ScreenCapture
{
    public static Capture Take(Rectangle bounds)
    {
        if (bounds.Width < 8 || bounds.Height < 8) throw new ArgumentException("Selecciona un área de al menos 8 × 8 píxeles.");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        byte[] png = stream.ToArray();
        return new Capture(png, ToPreview(png), bounds);
    }
    public static BitmapSource ToPreview(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
