using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Viso;

public sealed class ColorSpectrum : FrameworkElement
{
    private double hue, saturation, brightness = 1;
    private Color selectedColor = Colors.White;
    private BitmapSource? spectrum;
    public event EventHandler? SelectedColorChanged;

    public ColorSpectrum()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetName(this, "Espectro de color. Usa las flechas para elegir tono y saturación.");
    }

    public Color SelectedColor
    {
        get => selectedColor;
        set
        {
            selectedColor = value;
            double maximum = Math.Max(value.R, Math.Max(value.G, value.B));
            double minimum = Math.Min(value.R, Math.Min(value.G, value.B));
            double delta = maximum - minimum;
            brightness = maximum / 255;
            saturation = maximum == 0 ? 0 : delta / maximum;
            if (delta > 0)
            {
                hue = maximum == value.R ? ((value.G - value.B) / delta + 6) % 6 : maximum == value.G ? (value.B - value.R) / delta + 2 : (value.R - value.G) / delta + 4;
                hue /= 6;
            }
            spectrum = null;
            InvalidateVisual();
        }
    }

    public double Brightness
    {
        get => brightness;
        set { brightness = Math.Clamp(value, 0, 1); spectrum = null; SelectColor(); }
    }

    internal static Color FromHsv(double hue, double saturation, double value)
    {
        double sector = (hue % 1 + 1) % 1 * 6;
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        double offset = value - chroma;
        var (r, g, b) = (int)sector switch
        {
            0 => (chroma, x, 0d), 1 => (x, chroma, 0d), 2 => (0d, chroma, x),
            3 => (0d, x, chroma), 4 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + offset) * 255), (byte)Math.Round((g + offset) * 255), (byte)Math.Round((b + offset) * 255));
    }

    protected override void OnRender(DrawingContext drawing)
    {
        const int width = 360, height = 160;
        if (spectrum == null)
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var color = FromHsv((double)x / (width - 1), 1 - (double)y / (height - 1), brightness);
                    int index = (y * width + x) * 4;
                    pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
                }
            spectrum = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            spectrum.Freeze();
        }
        drawing.PushClip(new RectangleGeometry(new Rect(RenderSize), 8, 8));
        drawing.DrawImage(spectrum, new Rect(RenderSize));
        var center = new Point(Math.Clamp(hue * ActualWidth, Math.Min(8, ActualWidth / 2), Math.Max(ActualWidth / 2, ActualWidth - 8)), Math.Clamp((1 - saturation) * ActualHeight, Math.Min(8, ActualHeight / 2), Math.Max(ActualHeight / 2, ActualHeight - 8)));
        drawing.DrawEllipse(null, new Pen(Brushes.Black, 4), center, 6, 6);
        drawing.DrawEllipse(null, new Pen(Brushes.White, 2), center, 6, 6);
        if (IsKeyboardFocused) drawing.DrawRoundedRectangle(null, new Pen(Brushes.White, 2), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 8, 8);
        drawing.Pop();
    }

    private void SelectColor()
    {
        selectedColor = FromHsv(hue, saturation, brightness);
        InvalidateVisual();
        SelectedColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectPoint(Point point)
    {
        hue = Math.Clamp(point.X / Math.Max(1, ActualWidth), 0, 1);
        saturation = 1 - Math.Clamp(point.Y / Math.Max(1, ActualHeight), 0, 1);
        SelectColor();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus(); CaptureMouse(); SelectPoint(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SelectPoint(e.GetPosition(this));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) { SelectPoint(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled = true; }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: hue = Math.Max(0, hue - 1d / 360); break;
            case Key.Right: hue = Math.Min(1, hue + 1d / 360); break;
            case Key.Up: saturation = Math.Min(1, saturation + 0.01); break;
            case Key.Down: saturation = Math.Max(0, saturation - 0.01); break;
            default: return;
        }
        SelectColor(); e.Handled = true;
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
