using System;
using System.Drawing;

namespace Viso;

public sealed class CaptureArea
{
    public string ScreenDevice { get; set; } = "";
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public static CaptureArea Create(string device, Rectangle screen, Rectangle selected)
    {
        var area = new CaptureArea { ScreenDevice = device, ScreenWidth = screen.Width, ScreenHeight = screen.Height, X = selected.X - screen.X, Y = selected.Y - screen.Y, Width = selected.Width, Height = selected.Height };
        area.Resolve(device, screen);
        return area;
    }
    public Rectangle Resolve(string device, Rectangle screen)
    {
        if (ScreenDevice != device) throw new ArgumentException("Elige un área para este monitor.");
        if (screen.Width != ScreenWidth || screen.Height != ScreenHeight) throw new ArgumentException("Cambió la resolución. Vuelve a elegir el área.");
        if (X < 0 || Y < 0 || Width < 8 || Height < 8 || (long)X + Width > screen.Width || (long)Y + Height > screen.Height)
            throw new ArgumentException("El área no es válida. Vuelve a elegirla.");
        return new Rectangle(screen.X + X, screen.Y + Y, Width, Height);
    }
}
