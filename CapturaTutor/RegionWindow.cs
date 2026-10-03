using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace CapturaTutor;

public sealed class RegionWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private readonly Canvas canvas = new();
    private readonly Rectangle selection = new() { Stroke = new SolidColorBrush(Color.FromRgb(0, 122, 255)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(25, 0, 122, 255)), RadiusX = 4, RadiusY = 4 };
    private readonly Drawing.Rectangle bounds;
    private Point start;
    private NativePoint nativeStart;
    private bool selecting;
    public Drawing.Rectangle? Selected { get; private set; }

    public RegionWindow(Capture capture)
    {
        bounds = capture.Bounds;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = new ImageBrush(capture.Preview) { Stretch = Stretch.Fill };
        canvas.Background = new SolidColorBrush(Color.FromArgb(75, 0, 0, 0));
        Content = canvas;
        var hint = new Border { Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)), BorderBrush = new SolidColorBrush(Color.FromRgb(218, 218, 224)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 14, 20, 14), IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.16 },
            Child = new TextBlock { Text = "Arrastra para elegir un área · Esc para cancelar", Foreground = new SolidColorBrush(Color.FromRgb(29, 29, 31)), FontFamily = new FontFamily("SF Pro Text, Helvetica Neue, Segoe UI"), FontSize = 14 } };
        hint.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        hint.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ((TextBlock)hint.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        selection.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        Canvas.SetLeft(hint, 24); Canvas.SetTop(hint, 24);
        canvas.Children.Add(hint);
        canvas.Children.Add(selection);
        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014);
        Loaded += (_, _) => { SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014); Activate(); Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
        MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas);
            GetCursorPos(out nativeStart);
            selecting = true;
            canvas.CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (!selecting) return;
            var current = e.GetPosition(canvas);
            Canvas.SetLeft(selection, Math.Min(start.X, current.X));
            Canvas.SetTop(selection, Math.Min(start.Y, current.Y));
            selection.Width = Math.Abs(start.X - current.X);
            selection.Height = Math.Abs(start.Y - current.Y);
        };
        MouseLeftButtonUp += (_, _) =>
        {
            if (!selecting) return;
            selecting = false;
            canvas.ReleaseMouseCapture();
            GetCursorPos(out var end);
            var region = Drawing.Rectangle.FromLTRB(Math.Min(nativeStart.X, end.X), Math.Min(nativeStart.Y, end.Y), Math.Max(nativeStart.X, end.X), Math.Max(nativeStart.Y, end.Y));
            region.Intersect(bounds);
            if (region.Width < 8 || region.Height < 8) return;
            Selected = region;
            DialogResult = true;
        };
    }
}
