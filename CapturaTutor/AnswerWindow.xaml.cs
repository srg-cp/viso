using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace CapturaTutor;

public partial class AnswerWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    private const int OverlayStyles = 0x20 | 0x80 | 0x08000000;
    public string ResponseText => AnswerText.Text;
    public AnswerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | OverlayStyles);
        };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Present(string text, string model, string meta, Settings settings, Forms.Screen screen)
    {
        // Reuse one overlay; each response replaces the previous text.
        AnswerText.Text = text;
        ApplySettings(settings, screen);
        Show();
        Position(settings.Corner, screen);
    }
    public void Position(string corner, Forms.Screen screen)
    {
        // Screen coordinates are physical pixels. SetWindowPos avoids mixing per-monitor DPI units.
        IntPtr handle = new WindowInteropHelper(this).Handle;
        var work = screen.WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, work.Left + 14, work.Top + 14, 0, 0, 0x0015);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        Width = Math.Min(440, Math.Max(1, (work.Width - 28) / dpi.DpiScaleX));
        MaxHeight = Math.Max(1, (work.Height - 28) / dpi.DpiScaleY);
        UpdateLayout();
        GetWindowRect(handle, out var rect);
        int width = Math.Min(rect.Right - rect.Left, work.Width - 28);
        int height = Math.Min(rect.Bottom - rect.Top, work.Height - 28);
        int x = corner == "left" ? work.Left + 14 : work.Right - width - 14;
        SetWindowPos(handle, IntPtr.Zero, x, work.Bottom - height - 14, width, height, 0x0014);
    }
    public void ApplySettings(Settings settings, Forms.Screen screen)
    {
        Topmost = settings.Topmost;
        AnswerText.FontSize = settings.FontSize;
        AnswerText.TextAlignment = settings.Corner == "left" ? TextAlignment.Left : TextAlignment.Right;
        if (IsVisible) Position(settings.Corner, screen);
    }
    internal void VerifyPosition(string corner, Forms.Screen screen)
    {
        GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var handle = new WindowInteropHelper(this).Handle;
        if (!AllowsTransparency || WindowStyle != WindowStyle.None || ShowActivated || ShowInTaskbar || (GetWindowLong(handle, -20) & OverlayStyles) != OverlayStyles)
            throw new InvalidOperationException("La respuesta debe ser transparente, sin activación y permitir clics a través.");
        var work = screen.WorkingArea;
        int expected = corner == "left" ? work.Left + 14 : work.Right - (rect.Right - rect.Left) - 14;
        if (Math.Abs(rect.Left - expected) > 2 || Math.Abs(rect.Bottom - (work.Bottom - 14)) > 2)
            throw new InvalidOperationException("La ventana de respuesta no está en la esquina esperada.");
    }
}
