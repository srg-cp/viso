using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CapturaTutor;

internal static class TrayArtwork
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var blue = new SolidBrush(Color.FromArgb(0, 122, 255));
        using var rounded = new GraphicsPath();
        rounded.AddArc(1, 1, 12, 12, 180, 90); rounded.AddArc(19, 1, 12, 12, 270, 90);
        rounded.AddArc(19, 19, 12, 12, 0, 90); rounded.AddArc(1, 19, 12, 12, 90, 90); rounded.CloseFigure();
        graphics.FillPath(blue, rounded);
        using var white = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawRectangle(white, 7, 10, 18, 14);
        graphics.DrawEllipse(white, 12, 13, 8, 8);
        graphics.DrawLine(white, 11, 7, 18, 7);
        IntPtr handle = bitmap.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }
}
