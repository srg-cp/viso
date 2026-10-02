using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Navigation;
using Forms = System.Windows.Forms;

namespace CapturaTutor;

public partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
    private readonly Settings settings;
    private readonly VisionClient vision = new();
    private readonly AnswerWindow answer = new();
    private readonly Forms.NotifyIcon tray;
    private Capture? capture;
    private CancellationTokenSource? requestCancellation;
    private bool busy;
    private bool exiting;
    private IntPtr handle;
    private Forms.Screen? answerScreen;
    private const int HotkeyId = 0x4354;

    public MainWindow()
    {
        InitializeComponent();
        settings = Settings.Load(out string warning);
        foreach (var screen in Forms.Screen.AllScreens)
            MonitorBox.Items.Add(new ComboBoxItem { Content = $"Monitor {MonitorBox.Items.Count + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · principal" : ""), Tag = screen.DeviceName });
        MonitorBox.SelectedIndex = Math.Max(0, Forms.Screen.AllScreens.ToList().FindIndex(s => s.DeviceName == settings.ScreenDevice));
        AreaBox.SelectedIndex = settings.Region ? 1 : 0;
        PreviewCheck.IsChecked = settings.PreviewBeforeSend;
        RememberCheck.IsChecked = settings.RememberKey;
        ModelBox.Text = settings.Model;
        CornerBox.SelectedIndex = settings.Corner == "left" ? 1 : 0;
        TopmostCheck.IsChecked = settings.Topmost;
        FontBox.Text = settings.FontSize.ToString();
        InstructionsBox.Text = settings.Instructions;
        var task = TaskBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == settings.Task);
        if (task != null) TaskBox.SelectedItem = task;
        try { KeyBox.Password = settings.ReadKey(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { warning = "No se pudo descifrar la clave guardada. Introduce tu API key otra vez."; }
        ModelSummary.Text = settings.Model;
        StatusText.Text = !string.IsNullOrEmpty(warning) ? warning : string.IsNullOrEmpty(KeyBox.Password) ? "Configura tu API key para comenzar." : "Listo. Captura el ejercicio que quieras consultar.";
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "CapturaTutor · Alt + Mayús + S", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir CapturaTutor", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Capturar pantalla", null, (_, _) => Dispatcher.InvokeAsync(async () => await CaptureAsync()));
        menu.Items.Add("Ver respuesta", null, (_, _) => Dispatcher.Invoke(ShowAnswer));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
            if (!RegisterHotKey(handle, HotkeyId, 0x0001 | 0x0004 | 0x4000, 0x53))
                StatusText.Text = "El atajo Alt + Mayús + S está ocupado. Usa el botón o la bandeja.";
        };
        Closing += (_, _) =>
        {
            exiting = true;
            requestCancellation?.Cancel();
            UnregisterHotKey(handle, HotkeyId);
            tray.Visible = false;
            tray.Dispose();
            vision.Dispose();
            Application.Current.Shutdown();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Loaded += (_, _) =>
        {
            var screen = Forms.Screen.FromHandle(handle);
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double availableHeight = screen.WorkingArea.Height / scale.DpiScaleY;
            double availableWidth = screen.WorkingArea.Width / scale.DpiScaleX;
            MinHeight = Math.Min(MinHeight, availableHeight - 24);
            MinWidth = Math.Min(MinWidth, availableWidth - 24);
            Height = Math.Min(Height, availableHeight - 24);
            Width = Math.Min(Width, availableWidth - 24);
        };
    }
    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = CaptureAsync();
        }
        return IntPtr.Zero;
    }
    private Forms.Screen SelectedScreen()
    {
        string device = (MonitorBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }
    private void ReadSettings(bool persist)
    {
        settings.Model = ModelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new ArgumentException("Escribe o selecciona un modelo.");
        settings.Corner = CornerBox.SelectedIndex == 1 ? "left" : "right";
        settings.ScreenDevice = SelectedScreen().DeviceName;
        settings.Region = AreaBox.SelectedIndex == 1;
        settings.PreviewBeforeSend = PreviewCheck.IsChecked == true;
        settings.RememberKey = RememberCheck.IsChecked == true;
        settings.Topmost = TopmostCheck.IsChecked == true;
        settings.FontSize = int.TryParse(FontBox.Text, out int size) ? Math.Clamp(size, 12, 24) : 16;
        settings.Task = (TaskBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Resolver paso a paso";
        settings.Instructions = InstructionsBox.Text.Trim();
        ModelSummary.Text = settings.Model;
        if (persist) settings.Save(KeyBox.Password.Trim());
    }
    private void SetBusy(bool value)
    {
        busy = value;
        CaptureButton.IsEnabled = !value;
        SendButton.IsEnabled = !value && capture != null;
        CancelButton.Visibility = value && requestCancellation != null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowMain()
    {
        Show(); WindowState = WindowState.Normal; Activate();
    }
    private async void CaptureClick(object sender, RoutedEventArgs e) => await CaptureAsync();
    private async Task CaptureAsync()
    {
        if (busy || exiting) return;
        bool wasVisible = IsVisible;
        bool answerVisible = answer.IsVisible;
        bool captured = false;
        try
        {
            ReadSettings(true);
            var screen = SelectedScreen();
            bool region = settings.Region;
            bool preview = settings.PreviewBeforeSend;
            SetBusy(true);
            // Never allow a previous capture to be sent after a cancelled or failed recapture.
            capture = null;
            PreviewImage.Source = null;
            EmptyPreview.Visibility = Visibility.Visible;
            Hide(); answer.Hide();
            await Task.Delay(300);
            var next = ScreenCapture.Take(screen.Bounds);
            if (region)
            {
                var selector = new RegionWindow(next);
                if (selector.ShowDialog() != true || selector.Selected == null)
                { StatusText.Text = "Selección cancelada."; return; }
                // Crop the already captured image so the selector can never appear in the sent image.
                var relative = selector.Selected.Value;
                var cropped = new System.Windows.Media.Imaging.CroppedBitmap(next.Preview,
                    new Int32Rect(relative.X - next.Bounds.X, relative.Y - next.Bounds.Y, relative.Width, relative.Height));
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(cropped));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                byte[] png = stream.ToArray();
                next = new Capture(png, ScreenCapture.ToPreview(png), relative);
            }
            capture = next;
            PreviewImage.Source = next.Preview;
            EmptyPreview.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Captura lista: {next.Bounds.Width} × {next.Bounds.Height}.";
            captured = true;
            SetBusy(false);
            if (preview) ShowMain();
            else
            {
                await SendAsync();
                if (wasVisible && !exiting) ShowMain();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo capturar: " + ex.Message;
            ShowMain();
        }
        finally
        {
            if (!exiting)
            {
                SetBusy(false);
                if (!captured && wasVisible) ShowMain();
                if (!captured && answerVisible) answer.Show();
            }
        }
    }
    private async void SendClick(object sender, RoutedEventArgs e) => await SendAsync();
    private async Task SendAsync()
    {
        if (busy || capture == null || exiting) return;
        if (string.IsNullOrWhiteSpace(KeyBox.Password))
        {
            Tabs.SelectedIndex = 1;
            StatusText.Text = "Introduce tu API key de OpenAI y guarda la configuración.";
            ShowMain(); KeyBox.Focus(); return;
        }
        try
        {
            ReadSettings(true);
            string model = settings.Model;
            string task = settings.Task;
            string instructions = settings.Instructions;
            string key = KeyBox.Password.Trim();
            byte[] png = capture.Png;
            answerScreen = SelectedScreen();
            requestCancellation = new CancellationTokenSource();
            SetBusy(true);
            StatusText.Text = "Analizando el ejercicio… Puedes cancelar la espera.";
            answer.Present("Analizando la captura…\n\nEl modelo está preparando la respuesta.", model, "Consulta en curso", settings, answerScreen);
            var timer = Stopwatch.StartNew();
            var result = await vision.SolveAsync(png, key, model, task, instructions, requestCancellation.Token);
            if (exiting) return;
            timer.Stop();
            ReadSettings(false);
            answer.Present(result.Text, model, result.Usage + $" · {timer.Elapsed.TotalSeconds:F1} s", settings, answerScreen);
            StatusText.Text = "Respuesta lista en la ventana flotante.";
            ResultButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            if (exiting) return;
            string text = requestCancellation?.IsCancellationRequested == true ? "Consulta cancelada. Si ya se envió, el proveedor puede haber procesado la solicitud." : "El modelo tardó más de 3 minutos. Prueba una región más pequeña o vuelve a consultar.";
            StatusText.Text = text;
            answer.Present(text, settings.Model, "Consulta detenida", settings, answerScreen ?? SelectedScreen());
            ResultButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (exiting) return;
            string text = ex is HttpRequestException ? "No se pudo conectar con OpenAI. Comprueba tu conexión a Internet." : ex.Message;
            StatusText.Text = "La consulta no se completó. Revisa el detalle en la respuesta.";
            answer.Present(text, settings.Model, "No se pudo completar la consulta", settings, answerScreen ?? SelectedScreen());
            ResultButton.IsEnabled = true;
        }
        finally
        {
            requestCancellation?.Dispose(); requestCancellation = null;
            if (!exiting) SetBusy(false);
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => requestCancellation?.Cancel();
    private void ResultClick(object sender, RoutedEventArgs e) => ShowAnswer();
    private void ShowAnswer()
    {
        if (!ResultButton.IsEnabled && !busy) { ShowMain(); return; }
        ReadSettings(false);
        answer.Show(); answer.ApplySettings(settings, SelectedScreen());
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadSettings(true);
            answer.ApplySettings(settings, SelectedScreen());
            StatusText.Text = "Configuración guardada.";
        }
        catch (Exception ex) { StatusText.Text = "No se pudo guardar: " + ex.Message; }
    }
    private void TrayClick(object sender, RoutedEventArgs e) => Hide();
    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
    public async Task SmokeTestAsync()
    {
        await Task.Delay(400);
        var screen = SelectedScreen();
        Hide();
        await Task.Delay(300);
        var test = ScreenCapture.Take(new System.Drawing.Rectangle(screen.Bounds.X, screen.Bounds.Y, Math.Min(300, screen.Bounds.Width), Math.Min(200, screen.Bounds.Height)));
        if (test.Png.Length < 30 || test.Preview.PixelWidth != 300) throw new InvalidOperationException("Captura inválida.");
        settings.Corner = "right";
        answer.Present("Prueba local de interfaz.\n\n2x + 4 = 10\n2x = 6\nx = 3\n\nComprobación: 2(3) + 4 = 10.", "Prueba sin API", "No se envió ninguna imagen", settings, screen);
        await Task.Delay(250);
        answer.VerifyPosition("right", screen);
        settings.Corner = "left";
        answer.Position(settings.Corner, screen);
        await Task.Delay(250);
        answer.VerifyPosition("left", screen);
        ShowMain();
        UpdateLayout();
        var render = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        render.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
        using var uiFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-ui.png"));
        encoder.Save(uiFile);
    }
}
