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
    public AnswerWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }
    public void Present(string text, string model, string meta, Settings settings, Forms.Screen screen)
    {
        AnswerText.Text = text;
        AnswerText.FontSize = settings.FontSize;
        AnswerText.ScrollToHome();
        ModelText.Text = model;
        MetaText.Text = meta;
        Topmost = settings.Topmost;
        CopyButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
        ShowActivated = false;
        Show();
        Position(settings.Corner, screen);
    }
    public void Position(string corner, Forms.Screen screen)
    {
        // Screen coordinates are physical pixels. SetWindowPos avoids mixing per-monitor DPI units.
        IntPtr handle = new WindowInteropHelper(this).Handle;
        var work = screen.WorkingArea;
        SetWindowPos(handle, IntPtr.Zero, work.Left + 14, work.Top + 14, 0, 0, 0x0015);
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
        if (IsVisible) Position(settings.Corner, screen);
    }
    internal void VerifyPosition(string corner, Forms.Screen screen)
    {
        GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        var work = screen.WorkingArea;
        int expected = corner == "left" ? work.Left + 14 : work.Right - (rect.Right - rect.Left) - 14;
        if (Math.Abs(rect.Left - expected) > 2 || Math.Abs(rect.Bottom - (work.Bottom - 14)) > 2)
            throw new InvalidOperationException("La ventana de respuesta no está en la esquina esperada.");
    }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(AnswerText.Text); MetaText.Text = "Respuesta copiada al portapapeles."; }
        catch (ExternalException) { MetaText.Text = "Portapapeles ocupado. Inténtalo de nuevo."; }
    }
    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = "Respuesta-CapturaTutor.txt" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, AnswerText.Text);
    }
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
}
