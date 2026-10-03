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
using System.Windows.Input;
using System.Windows.Navigation;
using Forms = System.Windows.Forms;

namespace CapturaTutor;

public partial class MainWindow : Window
{
    private readonly Settings settings;
    private readonly VisionClient vision;
    private readonly bool persistSettings;
    private readonly OverlayFlowTestHandler? visionTestHandler;
    private readonly AnswerWindow answer = new();
    private readonly Forms.NotifyIcon tray;
    private Capture? capture;
    private CancellationTokenSource? requestCancellation;
    private bool busy;
    private bool hasResponse;
    private bool exiting;
    private IntPtr handle;
    private Forms.Screen? answerScreen;
    private GlobalHotkey? hotkey;
    private uint draftModifiers;
    private int draftVirtualKey;
    private bool configReady;
    private bool loadingProvider;
    private string activeProvider = "OpenAI";
    private string CurrentModel => ModelBox.Text.Trim();
    private string CurrentKey => ProviderBox.SelectedIndex == 1 ? GeminiKeyBox.Password.Trim() : KeyBox.Password.Trim();

    public MainWindow(HttpMessageHandler? testHandler = null, bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        visionTestHandler = testHandler as OverlayFlowTestHandler;
        vision = new VisionClient(testHandler);
        InitializeComponent();
        string warning = "";
        settings = persistSettings ? Settings.Load(out warning) : new Settings();
        foreach (var screen in Forms.Screen.AllScreens)
            MonitorBox.Items.Add(new ComboBoxItem { Content = $"Monitor {MonitorBox.Items.Count + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · principal" : ""), Tag = screen.DeviceName });
        MonitorBox.SelectedIndex = Math.Max(0, Forms.Screen.AllScreens.ToList().FindIndex(s => s.DeviceName == settings.ScreenDevice));
        AreaBox.SelectedIndex = settings.Region ? 1 : 0;
        draftModifiers = settings.HotkeyModifiers;
        draftVirtualKey = settings.HotkeyVirtualKey;
        HotkeyBox.Text = GlobalHotkey.Label(draftModifiers, draftVirtualKey);
        RememberCheck.IsChecked = settings.RememberKey;
        loadingProvider = true;
        ProviderBox.SelectedIndex = settings.Provider == "Gemini" ? 1 : 0;
        activeProvider = settings.Provider;
        PopulateModels();
        loadingProvider = false;
        CornerBox.SelectedIndex = settings.Corner == "left" ? 1 : 0;
        TopmostCheck.IsChecked = settings.Topmost;
        FontBox.Text = settings.FontSize.ToString();
        var task = TaskBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == settings.Task);
        if (task != null) TaskBox.SelectedItem = task;
        try { KeyBox.Password = settings.ReadKey(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { warning = "No se pudo descifrar la clave guardada. Introduce tu API key otra vez."; }
        try { GeminiKeyBox.Password = settings.ReadGeminiKey(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { warning = "No se pudo descifrar la clave de Gemini. Introdúcela otra vez."; }
        ModelSummary.Text = activeProvider + " · " + CurrentModel;
        StatusText.Text = !string.IsNullOrEmpty(warning) ? warning : string.IsNullOrEmpty(CurrentKey) ? "Configura tu API key para comenzar." : "Listo. Captura el enunciado.";
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "CapturaTutor", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir CapturaTutor", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("Capturar pantalla", null, (_, _) => Dispatcher.InvokeAsync(async () => await CaptureAsync()));
        menu.Items.Add("Ver respuesta", null, (_, _) => Dispatcher.Invoke(ShowAnswer));
        menu.Items.Add("Ocultar respuesta", null, (_, _) => Dispatcher.Invoke(answer.Hide));
        menu.Items.Add("Copiar respuesta", null, (_, _) => Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(answer.ResponseText))
                try { Clipboard.SetText(answer.ResponseText); }
                catch (ExternalException) { StatusText.Text = "Portapapeles ocupado. Inténtalo de nuevo."; }
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            ConfigureChrome();
            HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
            hotkey = new GlobalHotkey(handle);
            if (!hotkey.TryReplace(settings.HotkeyModifiers, settings.HotkeyVirtualKey, out string error)) StatusText.Text = error;
            UpdateHotkeyDisplay();
        };
        Closing += (_, _) =>
        {
            exiting = true;
            requestCancellation?.Cancel();
            hotkey?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            vision.Dispose();
            Application.Current.Shutdown();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        configReady = true;
        SelectSection(0, false);
        UpdateCaptureOptions();
        Loaded += (_, _) =>
        {
            AnimateEntrance(ShellContent, 0);
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
        if (HandleChromeMessage(message, lParam)) { handled = true; return IntPtr.Zero; }
        if (message == 0x0312 && hotkey?.Registered == true && wParam.ToInt32() == hotkey.Id)
        {
            handled = true;
            if (!HotkeyBox.IsKeyboardFocusWithin) _ = CaptureAsync();
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
        settings.Provider = activeProvider;
        if (string.IsNullOrWhiteSpace(CurrentModel)) throw new ArgumentException("Escribe o selecciona un modelo.");
        if (activeProvider == "Gemini") settings.GeminiModel = CurrentModel;
        else settings.Model = CurrentModel;
        settings.Corner = CornerBox.SelectedIndex == 1 ? "left" : "right";
        settings.ScreenDevice = SelectedScreen().DeviceName;
        settings.Region = AreaBox.SelectedIndex == 1;
        settings.PreviewBeforeSend = false;
        settings.RememberKey = RememberCheck.IsChecked == true;
        settings.Topmost = TopmostCheck.IsChecked == true;
        settings.FontSize = int.TryParse(FontBox.Text, out int size) ? Math.Clamp(size, 12, 24) : 16;
        settings.Task = (TaskBox.SelectedItem as ComboBoxItem)?.Content as string ?? TaskPrompts.Alternative;
        settings.Instructions = "";
        ModelSummary.Text = activeProvider + " · " + CurrentModel;
        if (persist && persistSettings) settings.Save(KeyBox.Password.Trim(), GeminiKeyBox.Password.Trim());
    }
    private void HotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (key == Key.Escape) { draftModifiers = settings.HotkeyModifiers; draftVirtualKey = settings.HotkeyVirtualKey; HotkeyBox.Text = GlobalHotkey.Label(draftModifiers, draftVirtualKey); UpdateHotkeyDisplay(); return; }
        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 1;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= 2;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 4;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= 8;
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!GlobalHotkey.IsValid(modifiers, virtualKey)) { HotkeyStatus.Text = "Usa Ctrl, Alt, Mayús o Windows junto con otra tecla."; return; }
        draftModifiers = modifiers; draftVirtualKey = virtualKey;
        HotkeyBox.Text = GlobalHotkey.Label(modifiers, virtualKey);
        HotkeyStatus.Text = "Pulsa Guardar configuración para activar este atajo. Esc restaura el anterior.";
    }
    private void UpdateHotkeyDisplay()
    {
        string label = GlobalHotkey.Label(settings.HotkeyModifiers, settings.HotkeyVirtualKey);
        bool enabled = hotkey?.Registered == true;
        HotkeyStatus.Text = enabled ? "Atajo activo: " + label : "Atajo no disponible. Elige otra combinación y guarda.";
        CaptureHint.Text = (enabled ? label + " para capturar y enviar. " : "Configura un atajo para capturar y enviar. ") + "La imagen se envía al proveedor elegido; el uso de la API puede tener costo.";
        string trayText = enabled ? "CapturaTutor · " + label : "CapturaTutor · atajo no disponible";
        tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }
    private void CaptureOptionChanged(object sender, SelectionChangedEventArgs e) => UpdateCaptureOptions();
    private void UpdateCaptureOptions()
    {
        if (!configReady) return;
        SelectAreaButton.IsEnabled = !busy && AreaBox.SelectedIndex == 1;
        if (AreaBox.SelectedIndex == 0) { AreaSummary.Text = "Se capturará el monitor completo. El área guardada se conserva para reutilizarla."; return; }
        if (settings.SavedArea == null) { AreaSummary.Text = "Selecciona y guarda un área una vez. Las siguientes capturas usarán ese mismo rectángulo."; return; }
        try
        {
            var screen = SelectedScreen();
            var bounds = settings.SavedArea.Resolve(screen.DeviceName, screen.Bounds);
            AreaSummary.Text = $"Área guardada: {bounds.Width} × {bounds.Height} píxeles · X {settings.SavedArea.X}, Y {settings.SavedArea.Y}. Se reutilizará en cada captura.";
        }
        catch (ArgumentException ex) { AreaSummary.Text = ex.Message; }
    }
    private async void SelectAreaClick(object sender, RoutedEventArgs e)
    {
        if (busy || exiting) return;
        bool showAnswer = answer.IsVisible;
        CaptureArea? previousArea = settings.SavedArea;
        try
        {
            ReadSettings(false);
            var screen = SelectedScreen();
            SetBusy(true);
            Hide(); answer.Hide();
            await Task.Delay(300);
            var selector = new RegionWindow(ScreenCapture.Take(screen.Bounds));
            if (selector.ShowDialog() != true || selector.Selected == null) { StatusText.Text = "Selección cancelada. Se conserva el área anterior."; return; }
            settings.SavedArea = CaptureArea.Create(screen.DeviceName, screen.Bounds, selector.Selected.Value);
            settings.Region = true;
            AreaBox.SelectedIndex = 1;
            settings.Save(KeyBox.Password.Trim(), GeminiKeyBox.Password.Trim());
            StatusText.Text = "Área guardada. Las próximas capturas usarán ese mismo rectángulo.";
        }
        catch (Exception ex) { settings.SavedArea = previousArea; StatusText.Text = "No se pudo guardar el área: " + ex.Message; }
        finally
        {
            if (!exiting) { SetBusy(false); ShowMain(); if (showAnswer) answer.Show(); UpdateCaptureOptions(); }
        }
    }
    private void PopulateModels()
    {
        OpenAiKeyPanel.Visibility = activeProvider == "OpenAI" ? Visibility.Visible : Visibility.Collapsed;
        GeminiKeyPanel.Visibility = activeProvider == "Gemini" ? Visibility.Visible : Visibility.Collapsed;
        ModelBox.Items.Clear();
        string[] models = activeProvider == "Gemini"
            ? new[] { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-2.5-flash-lite", "gemini-3.5-flash" }
            : new[] { "gpt-5.4-mini", "gpt-5.4" };
        foreach (string model in models) ModelBox.Items.Add(model);
        ModelBox.Text = activeProvider == "Gemini" ? settings.GeminiModel : settings.Model;
        ModelHelp.Text = activeProvider == "Gemini"
            ? "Razonamiento alto y lectura de imagen en alta resolución. Flash-Lite: menor costo. Flash: mayor capacidad. Este ajuste puede aumentar el tiempo y consumo. 2.5 Lite requiere acceso previo de tu cuenta."
            : "GPT-5.4 mini: opción económica. GPT-5.4: para preguntas más complejas.";
        ModelSummary.Text = activeProvider + " · " + CurrentModel;
    }
    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingProvider || settings == null || ModelBox == null) return;
        if (!string.IsNullOrWhiteSpace(CurrentModel))
        {
            if (activeProvider == "Gemini") settings.GeminiModel = CurrentModel;
            else settings.Model = CurrentModel;
        }
        activeProvider = ProviderBox.SelectedIndex == 1 ? "Gemini" : "OpenAI";
        settings.Provider = activeProvider;
        PopulateModels();
    }
    private void SetBusy(bool value)
    {
        busy = value;
        ConfigContent.IsEnabled = !value;
        SaveButton.IsEnabled = !value;
        CancelButton.Visibility = value && requestCancellation != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateCaptureOptions();
    }
    private void ShowMain()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private async Task CaptureAsync()
    {
        if (busy || exiting) return;
        bool wasVisible = IsVisible;
        bool answerVisible = answer.IsVisible;
        bool captured = false;
        try
        {
            // Never leave an old image ready to send after a failed recapture.
            capture = null;
            ReadSettings(true);
            var screen = SelectedScreen();
            var captureBounds = settings.Region
                ? (settings.SavedArea ?? throw new ArgumentException("Primero selecciona y guarda un área en Configuración.")).Resolve(screen.DeviceName, screen.Bounds)
                : screen.Bounds;
            SetBusy(true);
            Hide(); answer.Hide();
            await Task.Delay(300);
            var next = ScreenCapture.Take(captureBounds);
            capture = next;
            StatusText.Text = $"Captura lista: {next.Bounds.Width} × {next.Bounds.Height}.";
            captured = true;
            SetBusy(false);
            await SendAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo capturar: " + ex.Message;
            if (settings.Region) { SelectSection(1); }
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
    private async Task SendAsync()
    {
        if (busy || capture == null || exiting) return;
        if (string.IsNullOrWhiteSpace(CurrentKey))
        {
            SelectSection(2);
            StatusText.Text = $"Introduce tu API key de {activeProvider} y guarda la configuración.";
            ShowMain();
            if (activeProvider == "Gemini") GeminiKeyBox.Focus(); else KeyBox.Focus();
            return;
        }
        try
        {
            ReadSettings(true);
            string model = CurrentModel;
            string provider = activeProvider;
            string task = settings.Task;
            string instructions = settings.Instructions;
            string key = CurrentKey;
            byte[] png = capture.Png;
            answerScreen = SelectedScreen();
            requestCancellation = new CancellationTokenSource();
            SetBusy(true);
            StatusText.Text = "Analizando el ejercicio… Puedes cancelar la espera.";
            answer.Hide();
            var timer = Stopwatch.StartNew();
            var result = await vision.SolveAsync(png, key, model, task, instructions, requestCancellation.Token, provider);
            if (exiting) return;
            timer.Stop();
            ReadSettings(false);
            answer.Present(result.Text, model, result.Usage + $" · {timer.Elapsed.TotalSeconds:F1} s", settings, answerScreen);
            StatusText.Text = "Respuesta lista en la ventana flotante.";
            hasResponse = true;
        }
        catch (OperationCanceledException)
        {
            if (exiting) return;
            string text = requestCancellation?.IsCancellationRequested == true ? "Consulta cancelada. Si ya se envió, el proveedor puede haber procesado la solicitud." : "El modelo tardó más de 5 minutos. Prueba una región más pequeña o vuelve a consultar.";
            StatusText.Text = text;
            answer.Present(text, CurrentModel, "Consulta detenida", settings, answerScreen ?? SelectedScreen());
            hasResponse = true;
        }
        catch (Exception ex)
        {
            if (exiting) return;
            string text = ex is HttpRequestException ? "No se pudo conectar con el proveedor. Comprueba tu conexión a Internet." : ex.Message;
            StatusText.Text = "La consulta no se completó. Revisa el detalle en la respuesta.";
            answer.Present(text, CurrentModel, "No se pudo completar la consulta", settings, answerScreen ?? SelectedScreen());
            hasResponse = true;
        }
        finally
        {
            requestCancellation?.Dispose(); requestCancellation = null;
            if (!exiting) SetBusy(false);
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => requestCancellation?.Cancel();
    private void ShowAnswer()
    {
        if (!hasResponse && !busy) { ShowMain(); return; }
        ReadSettings(false);
        answer.Show(); answer.ApplySettings(settings, SelectedScreen());
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        uint previousModifiers = settings.HotkeyModifiers;
        int previousVirtualKey = settings.HotkeyVirtualKey;
        try
        {
            string error = "No se pudo registrar el atajo.";
            if (hotkey == null || !hotkey.TryReplace(draftModifiers, draftVirtualKey, out error))
            {
                StatusText.Text = hotkey == null ? "No se pudo registrar el atajo." : error;
                UpdateHotkeyDisplay();
                return;
            }
            settings.HotkeyModifiers = draftModifiers;
            settings.HotkeyVirtualKey = draftVirtualKey;
            ReadSettings(true);
            UpdateHotkeyDisplay();
            UpdateCaptureOptions();
            answer.ApplySettings(settings, SelectedScreen());
            StatusText.Text = "Configuración guardada.";
        }
        catch (Exception ex)
        {
            settings.HotkeyModifiers = previousModifiers; settings.HotkeyVirtualKey = previousVirtualKey;
            if (hotkey != null && !hotkey.TryReplace(previousModifiers, previousVirtualKey, out _)) hotkey.Dispose();
            UpdateHotkeyDisplay();
            StatusText.Text = "No se pudo guardar: " + ex.Message;
        }
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
        if (FindName("Tabs") != null || FindName("CaptureButton") != null || FindName("PreviewImage") != null)
            throw new InvalidOperationException("La interfaz debe tener solo Configuración.");
        if (WindowStyle != WindowStyle.None || FindName("CloseWindowButton") == null || FindName("MinimizeWindowButton") == null || FindName("ZoomWindowButton") == null)
            throw new InvalidOperationException("La ventana debe usar sus tres controles propios.");
        if (TaskBox.Items.Count != 2) throw new InvalidOperationException("Solo deben existir dos tareas.");
        TaskBox.SelectedIndex = 1;
        ReadSettings(false);
        if (settings.Task != TaskPrompts.Complete) throw new InvalidOperationException("No se aplicó el tipo de enunciado desde Configuración.");
        TaskBox.SelectedIndex = 0;
        ReadSettings(false);
        ModelNav.IsChecked = true;
        if (ModelPanel.Visibility != Visibility.Visible || GeneralPanel.Visibility != Visibility.Collapsed || PaneTitle.Text != "Modelo de IA")
            throw new InvalidOperationException("La navegación de ajustes no cambió el panel.");
        int originalProvider = ProviderBox.SelectedIndex;
        string originalOpenAiModel = settings.Model;
        string originalGeminiModel = settings.GeminiModel;
        ProviderBox.SelectedIndex = 0;
        ModelBox.Text = "gpt-5.4";
        ProviderBox.SelectedIndex = 1;
        if (ModelBox.Items.Count != 4 || !CurrentModel.StartsWith("gemini-")) throw new InvalidOperationException("Modelos de Gemini incorrectos.");
        ModelBox.Text = "gemini-3.1-flash-lite";
        ProviderBox.SelectedIndex = 0;
        if (CurrentModel != "gpt-5.4") throw new InvalidOperationException("Se perdió el modelo OpenAI al cambiar de proveedor.");
        ProviderBox.SelectedIndex = 1;
        if (CurrentModel != "gemini-3.1-flash-lite") throw new InvalidOperationException("Se perdió el modelo Gemini al cambiar de proveedor.");
        ModelBox.ApplyTemplate();
        if (ModelBox.Template.FindName("PART_EditableTextBox", ModelBox) is not TextBox modelInput)
            throw new InvalidOperationException("El selector de modelo debe permitir escribir un modelo personalizado.");
        modelInput.Text = "modelo-personalizado";
        if (CurrentModel != "modelo-personalizado") throw new InvalidOperationException("El nuevo diseño no permite editar el modelo.");
        ModelBox.Text = "gemini-3.1-flash-lite";
        loadingProvider = true;
        settings.Model = originalOpenAiModel;
        settings.GeminiModel = originalGeminiModel;
        ProviderBox.SelectedIndex = originalProvider;
        activeProvider = originalProvider == 1 ? "Gemini" : "OpenAI";
        settings.Provider = activeProvider;
        PopulateModels();
        loadingProvider = false;
        if (visionTestHandler != null)
        {
            KeyBox.Password = "local-test-key";
            ProviderBox.SelectedIndex = 0;
            ShowMain();
            var flow = CaptureAsync();
            await visionTestHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (IsVisible || answer.IsVisible || !busy) throw new InvalidOperationException("La captura automática abrió una ventana durante la consulta.");
            await CaptureAsync();
            if (visionTestHandler.RequestCount != 1) throw new InvalidOperationException("La pulsación repetida duplicó la consulta.");
            visionTestHandler.Complete("c. A.5.7");
            await flow;
            if (IsVisible || !answer.IsVisible || answer.ResponseText != "c. A.5.7") throw new InvalidOperationException("El flujo no mostró solo la respuesta.");
            var overlayHandle = new WindowInteropHelper(answer).Handle;
            visionTestHandler.Reset();
            flow = CaptureAsync();
            await visionTestHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (answer.IsVisible) throw new InvalidOperationException("La respuesta anterior apareció en la nueva captura.");
            visionTestHandler.Complete("b. Segunda respuesta");
            await flow;
            if (IsVisible || answer.ResponseText != "b. Segunda respuesta" || new WindowInteropHelper(answer).Handle != overlayHandle || visionTestHandler.RequestCount != 2)
                throw new InvalidOperationException("La segunda consulta no reemplazó la respuesta en el mismo panel.");
        }
        var screen = SelectedScreen();
        Hide();
        await Task.Delay(300);
        var test = ScreenCapture.Take(new System.Drawing.Rectangle(screen.Bounds.X, screen.Bounds.Y, Math.Min(300, screen.Bounds.Width), Math.Min(200, screen.Bounds.Height)));
        if (test.Png.Length < 30 || test.Preview.PixelWidth != 300) throw new InvalidOperationException("Captura inválida.");
        var fixedArea = CaptureArea.Create(screen.DeviceName, screen.Bounds, new System.Drawing.Rectangle(screen.Bounds.X + 30, screen.Bounds.Y + 40, 100, 80));
        var restoredArea = System.Text.Json.JsonSerializer.Deserialize<CaptureArea>(System.Text.Json.JsonSerializer.Serialize(fixedArea))!;
        var fixedCapture = ScreenCapture.Take(restoredArea.Resolve(screen.DeviceName, screen.Bounds));
        if (fixedCapture.Preview.PixelWidth != 100 || fixedCapture.Preview.PixelHeight != 80) throw new InvalidOperationException("La captura no respetó el área guardada.");
        using (var firstWindow = new HwndSource(new HwndSourceParameters("CapturaTutorShortcutTest1") { ParentWindow = new IntPtr(-3) }))
        using (var secondWindow = new HwndSource(new HwndSourceParameters("CapturaTutorShortcutTest2") { ParentWindow = new IntPtr(-3) }))
        using (var firstKey = new GlobalHotkey(firstWindow.Handle))
        using (var secondKey = new GlobalHotkey(secondWindow.Handle))
        {
            int chosen = 0;
            for (int key = 0x7C; key <= 0x87; key++)
                if (firstKey.TryReplace(7, key, out _)) { chosen = key; break; }
            if (chosen == 0) throw new InvalidOperationException("No se encontró un atajo libre para la prueba.");
            if (secondKey.TryReplace(7, chosen, out _)) throw new InvalidOperationException("No se detectó un atajo ocupado.");
            int replacement = 0;
            for (int key = 0x7C; key <= 0x87; key++)
                if (key != chosen && secondKey.TryReplace(7, key, out _)) { replacement = key; break; }
            if (replacement == 0) throw new InvalidOperationException("No se encontró un segundo atajo libre.");
            if (firstKey.TryReplace(7, replacement, out _) || firstKey.VirtualKey != chosen || !firstKey.Registered)
                throw new InvalidOperationException("Un conflicto eliminó el atajo anterior.");
            secondKey.Dispose();
            if (!firstKey.TryReplace(7, replacement, out _)) throw new InvalidOperationException("No se pudo cambiar a un atajo libre.");
            if (!secondKey.TryReplace(7, chosen, out _)) throw new InvalidOperationException("El atajo anterior no fue liberado.");
        }
        settings.Corner = "right";
        answer.Present("Prueba local de interfaz.\n\n2x + 4 = 10\n2x = 6\nx = 3\n\nComprobación: 2(3) + 4 = 10.", "Prueba sin API", "No se envió ninguna imagen", settings, screen);
        await Task.Delay(250);
        answer.VerifyPosition("right", screen);
        settings.Corner = "left";
        answer.Position(settings.Corner, screen);
        await Task.Delay(250);
        answer.VerifyPosition("left", screen);
        answer.Present("c. A.5.7", "Prueba sin API", "", settings, screen);
        await Task.Delay(100);
        answer.VerifyPosition("left", screen);
        var overlayRender = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(answer.ActualWidth), (int)Math.Ceiling(answer.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        overlayRender.Render(answer);
        var pixels = new byte[overlayRender.PixelWidth * overlayRender.PixelHeight * 4];
        overlayRender.CopyPixels(pixels, overlayRender.PixelWidth * 4, 0);
        int transparent = 0, visible = 0;
        for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] == 0) transparent++; else visible++; }
        if (visible == 0 || transparent < overlayRender.PixelWidth * overlayRender.PixelHeight / 2)
            throw new InvalidOperationException("El panel no muestra texto sobre un fondo transparente.");
        var overlayEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        overlayEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(overlayRender));
        using (var overlayFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-overlay.png"))) overlayEncoder.Save(overlayFile);
        ShowMain();
        GeneralNav.IsChecked = true;
        await Task.Delay(250);
        UpdateLayout();
        var render = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        render.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
        using var uiFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-ui.png"));
        encoder.Save(uiFile);
        uiFile.Close();
        ConfigScroll.ScrollToTop();
        ProviderBox.SelectedIndex = 1;
        ModelNav.IsChecked = true;
        await Task.Delay(250);
        UpdateLayout();
        var configRender = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        configRender.Render(this);
        var configEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        configEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(configRender));
        using var configFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-config.png"));
        configEncoder.Save(configFile);
        configFile.Close();
        double originalWidth = Width;
        Width = 680;
        await Task.Delay(150);
        if (SidebarColumn.Width.Value != 84 || GeneralNavText.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("La ventana compacta no adaptó la barra lateral.");
        CaptureNav.IsChecked = true;
        await Task.Delay(250);
        var compactRender = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        compactRender.Render(this);
        var compactEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        compactEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(compactRender));
        using (var compactFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-compact.png"))) compactEncoder.Save(compactFile);
        Width = originalWidth;
        ZoomWindowClick(this, new RoutedEventArgs());
        await Task.Delay(150);
        if (WindowState != WindowState.Maximized) throw new InvalidOperationException("El botón verde no maximiza.");
        ZoomWindowClick(this, new RoutedEventArgs());
        await Task.Delay(150);
        if (WindowState != WindowState.Normal || SidebarColumn.Width.Value != 220) throw new InvalidOperationException("El botón verde no restaura el tamaño.");
        MinimizeWindowClick(this, new RoutedEventArgs());
        await Task.Delay(100);
        if (IsVisible) throw new InvalidOperationException("El botón amarillo no minimiza a la bandeja.");
        ShowMain();
    }
}
