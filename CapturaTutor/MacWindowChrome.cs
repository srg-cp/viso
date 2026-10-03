using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CapturaTutor;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void ConfigureChrome()
    {
        // Use native Windows resizing and shadows behind our own title bar.
        int rounded = 2;
        DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        UpdateResponsiveLayout();
    }
    private bool HandleChromeMessage(int message, IntPtr lParam)
    {
        if (message != 0x0024) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return false;
        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        limits.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        limits.MaxSize.X = info.Work.Right - info.Work.Left;
        limits.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        var dpi = VisualTreeHelper.GetDpi(this);
        limits.MinTrackSize.X = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX);
        limits.MinTrackSize.Y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY);
        Marshal.StructureToPtr(limits, lParam, false);
        return true;
    }
    private void UpdateResponsiveLayout()
    {
        bool compact = ActualWidth < 780;
        SidebarColumn.Width = new GridLength(compact ? 84 : 220);
        var visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarBrand.Visibility = SidebarFooter.Visibility = visibility;
        GeneralNavText.Visibility = CaptureNavText.Visibility = ModelNavText.Visibility = ResponseNavText.Visibility = visibility;
        bool maximized = WindowState == WindowState.Maximized;
        WindowSurface.CornerRadius = new CornerRadius(maximized ? 0 : 14);
        ZoomWindowButton.ToolTip = maximized ? "Restaurar tamaño" : "Maximizar";
    }
    private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is Button || FindButtonAncestor(e.OriginalSource as DependencyObject)) return;
        if (e.ClickCount == 2) ToggleZoom();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private static bool FindButtonAncestor(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Button) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private void CloseWindowClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeWindowClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ZoomWindowClick(object sender, RoutedEventArgs e) => ToggleZoom();
    private void ToggleZoom()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateResponsiveLayout();
    }
    private void NavigationChecked(object sender, RoutedEventArgs e)
    {
        if (!configReady || sender is not RadioButton item || !int.TryParse(item.Tag as string, out int index)) return;
        SelectSection(index);
    }
    private void SelectSection(int index, bool animate = true)
    {
        Border[] panels = { GeneralPanel, CaptureOptionsPanel, ModelPanel, ResponsePanel };
        RadioButton[] navigation = { GeneralNav, CaptureNav, ModelNav, ResponseNav };
        string[] titles = { "General", "Captura y atajo", "Modelo de IA", "Respuesta" };
        if (index < 0 || index >= panels.Length) return;
        for (int i = 0; i < panels.Length; i++) panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        PaneTitle.Text = titles[index];
        if (navigation[index].IsChecked != true) navigation[index].IsChecked = true;
        ConfigScroll.ScrollToTop();
        if (animate) AnimateEntrance(ConfigContent, 10);
    }
    private static void AnimateEntrance(FrameworkElement element, double offset)
    {
        if (!SystemParameters.ClientAreaAnimation) { element.Opacity = 1; return; }
        var duration = TimeSpan.FromMilliseconds(190);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (element.RenderTransform is TranslateTransform translate)
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });
    }
}
