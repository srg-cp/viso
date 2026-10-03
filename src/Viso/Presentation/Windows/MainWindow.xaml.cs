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
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Viso;

public partial class MainWindow : Window
{
    private readonly Settings settings;
    private readonly VisionClient vision;
    private readonly bool persistSettings;
    private readonly OverlayFlowTestHandler? visionTestHandler;
    private readonly AnswerWindow answer = new();
    private readonly Forms.NotifyIcon tray;
    private readonly ContextMenu trayMenu;
    private readonly ToolTip trayHint;
    private readonly DispatcherTimer trayHintTimer;
    private Capture? capture;
    private CancellationTokenSource? requestCancellation;
    private bool busy;
    private bool hasResponse;
    private bool exiting;
    private IntPtr handle;
    private Forms.Screen? answerScreen;
    private CaptureHotkeys? hotkeys;
    private GlobalHotkey? hotkey => hotkeys?.Full;
    private uint draftHideModifiers;
    private int draftHideVirtualKey;
    private uint draftShortModifiers;
    private int draftShortVirtualKey;
    private uint draftModifiers;
    private int draftVirtualKey;
    private bool configReady;
    private bool updatingColor;
    private bool loadingProvider;
    private string activeProvider = "OpenAI";
    private string CurrentModel => ModelBox.SelectedItem as string ?? "";
    private string CurrentKey => ProviderBox.SelectedIndex == 1 ? GeminiKeyBox.Password.Trim() : KeyBox.Password.Trim();

    public MainWindow(HttpMessageHandler? testHandler = null, bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        visionTestHandler = testHandler as OverlayFlowTestHandler;
        vision = new VisionClient(testHandler);
        InitializeComponent();
        PrepareTransitions();
        string warning = "";
        settings = persistSettings ? Settings.Load(out warning) : new Settings();
        if (!persistSettings)
        {
            // Smoke tests use separate shortcuts so a running installation can keep its own.
            settings.HotkeyModifiers = settings.ShortHotkeyModifiers = settings.HideHotkeyModifiers = 7;
            settings.HotkeyVirtualKey = 0x7F; settings.ShortHotkeyVirtualKey = 0x80; settings.HideHotkeyVirtualKey = 0x81;
        }
        DarkModeSwitch.IsChecked = settings.Appearance == "dark";
        Appearance.Apply(settings.Appearance);
        AppearanceIcon.Text = Appearance.IsDark ? "☾" : "☀";
        foreach (var screen in Forms.Screen.AllScreens)
            MonitorBox.Items.Add(new ComboBoxItem { Content = $"Monitor {MonitorBox.Items.Count + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · principal" : ""), Tag = screen.DeviceName });
        MonitorBox.SelectedIndex = Math.Max(0, Forms.Screen.AllScreens.ToList().FindIndex(s => s.DeviceName == settings.ScreenDevice));
        AreaBox.SelectedIndex = settings.Region ? 1 : 0;
        draftModifiers = settings.HotkeyModifiers;
        draftVirtualKey = settings.HotkeyVirtualKey;
        HotkeyBox.Text = GlobalHotkey.Label(draftModifiers, draftVirtualKey);
        draftShortModifiers = settings.ShortHotkeyModifiers;
        draftShortVirtualKey = settings.ShortHotkeyVirtualKey;
        ShortHotkeyBox.Text = GlobalHotkey.Label(draftShortModifiers, draftShortVirtualKey);
        MaxCharactersBox.Text = settings.MaxResponseCharacters.ToString();
        draftHideModifiers = settings.HideHotkeyModifiers;
        draftHideVirtualKey = settings.HideHotkeyVirtualKey;
        HideHotkeyBox.Text = GlobalHotkey.Label(draftHideModifiers, draftHideVirtualKey);
        DismissSecondsBox.Text = settings.DismissSeconds.ToString();
        (settings.DismissMode == ResponseDismissal.Click ? DismissClickChoice : settings.DismissMode == ResponseDismissal.Scroll ? DismissScrollChoice : DismissTimeChoice).IsChecked = true;
        answer.Dismissal.Warning += message => StatusText.Text = message;
        RememberCheck.IsChecked = settings.RememberKey;
        loadingProvider = true;
        ProviderBox.SelectedIndex = settings.Provider == "Gemini" ? 1 : 0;
        activeProvider = settings.Provider;
        PopulateModels();
        loadingProvider = false;
        CornerBox.SelectedIndex = settings.Corner == "left" ? 1 : 0;
        TopmostCheck.IsChecked = settings.Topmost;
        FontBox.Text = settings.FontSize.ToString();
        var textColor = ResponseTextStyle.ColorFor(settings.TextColor);
        RedBox.Text = textColor.R.ToString();
        GreenBox.Text = textColor.G.ToString();
        BlueBox.Text = textColor.B.ToString();
        DefaultTextColorCheck.IsChecked = settings.TextColor == null;
        var task = TaskBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((string?)i.Tag == "complete" ? TaskPrompts.Complete : TaskPrompts.Alternative) == settings.Task);
        if (task != null) TaskBox.SelectedItem = task;
        try { KeyBox.Password = settings.ReadKey(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { warning = "Vuelve a introducir tu API key de OpenAI."; }
        try { GeminiKeyBox.Password = settings.ReadGeminiKey(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        { warning = "Vuelve a introducir tu API key de Gemini."; }
        ModelSummary.Text = activeProvider + " · " + CurrentModel;
        StatusText.Text = !string.IsNullOrEmpty(warning) ? warning : string.IsNullOrEmpty(CurrentKey) ? "Añade tu API key." : "Listo.";
        tray = new Forms.NotifyIcon { Icon = TrayArtwork.Create(), Text = "", Visible = true };
        trayMenu = new ContextMenu { Placement = PlacementMode.MousePoint };
        void AddMenuItem(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { trayMenu.IsOpen = false; action(); }; trayMenu.Items.Add(item);
        }
        AddMenuItem("Abrir Viso", ShowMain);
        trayMenu.Items.Add(new Separator());
        AddMenuItem("Capturar · completa", () => _ = CaptureAsync());
        AddMenuItem("Capturar · recortada", () => _ = CaptureAsync(true));
        AddMenuItem("Ver respuesta", ShowAnswer);
        AddMenuItem("Ocultar respuesta", answer.Hide);
        AddMenuItem("Copiar respuesta", () =>
        {
            if (!string.IsNullOrWhiteSpace(answer.ResponseText))
                try { Clipboard.SetText(answer.ResponseText); }
                catch (ExternalException) { StatusText.Text = "No se pudo copiar. Inténtalo de nuevo."; }
        });
        AddMenuItem("Cancelar consulta", () => requestCancellation?.Cancel());
        trayMenu.Items.Add(new Separator());
        AddMenuItem("Salir", Close);
        trayHint = new ToolTip { Content = "Viso", Placement = PlacementMode.MousePoint, StaysOpen = true };
        trayHintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        trayHintTimer.Tick += (_, _) => { trayHint.IsOpen = false; trayHintTimer.Stop(); };
        tray.MouseMove += (_, _) => Dispatcher.Invoke(() =>
        {
            if (trayMenu.IsOpen || exiting) return;
            trayHint.IsOpen = true; trayHintTimer.Stop(); trayHintTimer.Start();
        });
        tray.MouseUp += (_, e) => Dispatcher.Invoke(() =>
        {
            trayHint.IsOpen = false; trayHintTimer.Stop();
            if (e.Button == Forms.MouseButtons.Right) trayMenu.IsOpen = true;
        });
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { trayHint.IsOpen = false; ShowMain(); });
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            ConfigureChrome();
            HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
            hotkeys = new CaptureHotkeys(handle);
            hotkeys.Initialize(settings.HotkeyModifiers, settings.HotkeyVirtualKey, settings.ShortHotkeyModifiers, settings.ShortHotkeyVirtualKey, settings.HideHotkeyModifiers, settings.HideHotkeyVirtualKey, out string error);
            if (!string.IsNullOrEmpty(error)) StatusText.Text = error;
            UpdateHotkeyDisplay();
        };
        Closing += (_, _) =>
        {
            exiting = true;
            requestCancellation?.Cancel();
            hotkeys?.Dispose();
            answer.Dismissal.Dispose();
            trayMenu.IsOpen = false; trayHint.IsOpen = false; trayHintTimer.Stop();
            tray.Visible = false;
            tray.Icon?.Dispose();
            tray.Dispose();
            vision.Dispose();
            Application.Current.Shutdown();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        configReady = true;
        foreach (var box in new[] { CornerBox, FontBox, TaskBox })
            box.SelectionChanged += (_, _) => UpdateResponsePreview();
        foreach (var check in new ToggleButton[] { DefaultTextColorCheck, TopmostCheck, PreviewShortCheck, DismissTimeChoice, DismissClickChoice, DismissScrollChoice })
        {
            check.Checked += (_, _) => UpdateResponsePreview();
            check.Unchecked += (_, _) => UpdateResponsePreview();
        }
        foreach (var box in new[] { MaxCharactersBox, DismissSecondsBox })
            box.TextChanged += (_, _) => UpdateResponsePreview();
        foreach (var box in new[] { RedBox, GreenBox, BlueBox })
            box.TextChanged += (_, _) => RgbColorChanged();
        TextColorSpectrum.SelectedColor = textColor;
        ColorBrightness.Value = TextColorSpectrum.Brightness;
        TextColorSpectrum.SelectedColorChanged += (_, _) => ApplyPickedColor();
        ColorBrightness.ValueChanged += (_, _) =>
        {
            if (!updatingColor) TextColorSpectrum.Brightness = ColorBrightness.Value;
        };
        DefaultTextColorCheck.Checked += (_, _) =>
        {
            if (!updatingColor) SetColorControls(System.Windows.Media.Colors.White);
        };
        UpdateResponsePreview();
        SelectSection(0, false);
        UpdateCaptureOptions();
        Loaded += (_, _) =>
        {
            AnimateWindowOpen();
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
        if (message == 0x0312)
        {
            int id = wParam.ToInt32();
            bool full = hotkeys?.Full?.Registered == true && id == hotkeys.Full.Id;
            bool shortAnswer = hotkeys?.Short?.Registered == true && id == hotkeys.Short.Id;
            bool hide = hotkeys?.Hide?.Registered == true && id == hotkeys.Hide.Id;
            if (full || shortAnswer || hide)
            {
                handled = true;
                if (!HotkeyBox.IsKeyboardFocusWithin && !ShortHotkeyBox.IsKeyboardFocusWithin && !HideHotkeyBox.IsKeyboardFocusWithin)
                {
                    if (hide) answer.Hide();
                    else _ = CaptureAsync(shortAnswer);
                }
            }
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
        if (!TryReadTextColor(out string? textColor))
            throw new ArgumentException("Cada componente RGB debe ser un número entero entre 0 y 255.");
        settings.TextColor = textColor;
        settings.Appearance = DarkModeSwitch.IsChecked == true ? "dark" : "light";
        if (!int.TryParse(MaxCharactersBox.Text, out int maxCharacters) || maxCharacters < 1 || maxCharacters > 10000)
            throw new ArgumentException("El límite debe estar entre 1 y 10.000 caracteres.");
        settings.MaxResponseCharacters = maxCharacters;
        string dismissMode = DismissClickChoice.IsChecked == true ? ResponseDismissal.Click : DismissScrollChoice.IsChecked == true ? ResponseDismissal.Scroll : ResponseDismissal.Time;
        bool validSeconds = int.TryParse(DismissSecondsBox.Text, out int seconds) && seconds >= 1 && seconds <= 3600;
        if (dismissMode == ResponseDismissal.Time && !validSeconds) throw new ArgumentException("El tiempo debe estar entre 1 y 3.600 segundos.");
        settings.DismissMode = dismissMode;
        if (validSeconds) settings.DismissSeconds = seconds;
        settings.Provider = activeProvider;
        if (string.IsNullOrWhiteSpace(CurrentModel)) throw new ArgumentException("Selecciona un modelo.");
        if (activeProvider == "Gemini") settings.GeminiModel = CurrentModel;
        else settings.Model = CurrentModel;
        settings.Corner = CornerBox.SelectedIndex == 1 ? "left" : "right";
        settings.ScreenDevice = SelectedScreen().DeviceName;
        settings.Region = AreaBox.SelectedIndex == 1;
        settings.PreviewBeforeSend = false;
        settings.RememberKey = RememberCheck.IsChecked == true;
        settings.Topmost = TopmostCheck.IsChecked == true;
        settings.FontSize = SelectedResponseFontSize();
        settings.Task = (TaskBox.SelectedItem as ComboBoxItem)?.Tag as string == "complete" ? TaskPrompts.Complete : TaskPrompts.Alternative;
        settings.Instructions = "";
        ModelSummary.Text = activeProvider + " · " + CurrentModel;
        if (persist && persistSettings) settings.Save(KeyBox.Password.Trim(), GeminiKeyBox.Password.Trim());
    }
    private bool TryReadTextColor(out string? color)
    {
        color = null;
        if (DefaultTextColorCheck.IsChecked == true) return true;
        if (!byte.TryParse(RedBox.Text, out byte red) || !byte.TryParse(GreenBox.Text, out byte green) || !byte.TryParse(BlueBox.Text, out byte blue)) return false;
        color = $"#{red:X2}{green:X2}{blue:X2}";
        return true;
    }

    private int SelectedResponseFontSize()
        => int.TryParse((FontBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? FontBox.Text, out int size) ? Math.Clamp(size, 12, 24) : 16;

    private void SetColorControls(System.Windows.Media.Color color)
    {
        updatingColor = true;
        try
        {
            RedBox.Text = color.R.ToString(); GreenBox.Text = color.G.ToString(); BlueBox.Text = color.B.ToString();
            TextColorSpectrum.SelectedColor = color;
            ColorBrightness.Value = TextColorSpectrum.Brightness;
        }
        finally { updatingColor = false; }
        UpdateResponsePreview();
    }

    private void ApplyPickedColor()
    {
        if (updatingColor) return;
        updatingColor = true;
        try
        {
            DefaultTextColorCheck.IsChecked = false;
            var color = TextColorSpectrum.SelectedColor;
            RedBox.Text = color.R.ToString(); GreenBox.Text = color.G.ToString(); BlueBox.Text = color.B.ToString();
        }
        finally { updatingColor = false; }
        UpdateResponsePreview();
    }

    private void RgbColorChanged()
    {
        if (updatingColor) return;
        DefaultTextColorCheck.IsChecked = false;
        if (TryReadTextColor(out string? color))
        {
            updatingColor = true;
            try
            {
                TextColorSpectrum.SelectedColor = ResponseTextStyle.ColorFor(color);
                ColorBrightness.Value = TextColorSpectrum.Brightness;
            }
            finally { updatingColor = false; }
        }
        UpdateResponsePreview();
    }

    private void UpdateResponsePreview()
    {
        if (!configReady) return;
        bool validColor = TryReadTextColor(out string? color);
        TextColorHelp.Text = validColor ? "Elige un color en el espectro o introduce valores RGB de 0 a 255." : "Introduce valores enteros de 0 a 255. La vista previa conserva el último color válido.";
        var draft = new Settings
        {
            FontSize = SelectedResponseFontSize(),
            Corner = CornerBox.SelectedIndex == 1 ? "left" : "right",
            TextColor = color
        };
        var previousBrush = ResponsePreviewText.Foreground;
        ResponseTextStyle.Apply(ResponsePreviewText, draft);
        if (!validColor) ResponsePreviewText.Foreground = previousBrush;
        TextColorSwatch.Background = ResponsePreviewText.Foreground;
        ResponsePreviewText.HorizontalAlignment = draft.Corner == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        string sample = TaskBox.SelectedIndex == 1 ? "La respuesta es: fotosíntesis." : "B. La fotosíntesis transforma la energía luminosa en energía química.";
        bool validLimit = int.TryParse(MaxCharactersBox.Text, out int limit) && limit >= 1 && limit <= 10000;
        ResponsePreviewText.Text = PreviewShortCheck.IsChecked == true && validLimit ? ResponseDisplay.Limit(sample, limit) : sample;
        string dismissal = DismissClickChoice.IsChecked == true ? "Se oculta con un clic." : DismissScrollChoice.IsChecked == true ? "Se oculta con el scroll." : int.TryParse(DismissSecondsBox.Text, out int seconds) && seconds >= 1 && seconds <= 3600 ? $"Se oculta tras {seconds} s." : "Introduce un tiempo entre 1 y 3.600 segundos.";
        ResponsePreviewSummary.Text = $"{(draft.Corner == "left" ? "Inferior izquierda" : "Inferior derecha")} · {draft.FontSize} px · {(TopmostCheck.IsChecked == true ? "Sobre otras ventanas" : "Sin prioridad sobre otras ventanas")}. {dismissal}";
        if (PreviewShortCheck.IsChecked == true && !validLimit) ResponsePreviewSummary.Text += " Introduce un límite entre 1 y 10.000 caracteres.";
    }

    private void HotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        bool shortAnswer = ReferenceEquals(sender, ShortHotkeyBox);
        bool hide = ReferenceEquals(sender, HideHotkeyBox);
        var status = hide ? HideHotkeyStatus : shortAnswer ? ShortHotkeyStatus : HotkeyStatus;
        if (key == Key.Escape)
        {
            if (hide) { draftHideModifiers = settings.HideHotkeyModifiers; draftHideVirtualKey = settings.HideHotkeyVirtualKey; HideHotkeyBox.Text = GlobalHotkey.Label(draftHideModifiers, draftHideVirtualKey); }
            else if (shortAnswer) { draftShortModifiers = settings.ShortHotkeyModifiers; draftShortVirtualKey = settings.ShortHotkeyVirtualKey; ShortHotkeyBox.Text = GlobalHotkey.Label(draftShortModifiers, draftShortVirtualKey); }
            else { draftModifiers = settings.HotkeyModifiers; draftVirtualKey = settings.HotkeyVirtualKey; HotkeyBox.Text = GlobalHotkey.Label(draftModifiers, draftVirtualKey); }
            UpdateHotkeyDisplay(); return;
        }
        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= 1;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= 2;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= 4;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= 8;
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!GlobalHotkey.IsValid(modifiers, virtualKey)) { status.Text = "Combina Ctrl, Alt, Mayús o Windows con otra tecla."; return; }
        if (hide) { draftHideModifiers = modifiers; draftHideVirtualKey = virtualKey; }
        else if (shortAnswer) { draftShortModifiers = modifiers; draftShortVirtualKey = virtualKey; }
        else { draftModifiers = modifiers; draftVirtualKey = virtualKey; }
        ((TextBox)sender).Text = GlobalHotkey.Label(modifiers, virtualKey);
        status.Text = "Sin guardar. Esc para cancelar.";
    }
    private void UpdateHotkeyDisplay()
    {
        string label = GlobalHotkey.Label(settings.HotkeyModifiers, settings.HotkeyVirtualKey);
        bool enabled = hotkey?.Registered == true;
        HotkeyStatus.Text = enabled ? "Activo: " + label : "Atajo ocupado. Elige otro.";
        string shortLabel = GlobalHotkey.Label(settings.ShortHotkeyModifiers, settings.ShortHotkeyVirtualKey);
        ShortHotkeyStatus.Text = hotkeys?.Short?.Registered == true ? "Activo: " + shortLabel : "Atajo ocupado. Elige otro.";
        string hideLabel = GlobalHotkey.Label(settings.HideHotkeyModifiers, settings.HideHotkeyVirtualKey);
        HideHotkeyStatus.Text = hotkeys?.Hide?.Registered == true ? "Activo: " + hideLabel : "Atajo ocupado. Elige otro.";
        CaptureHint.Text = "Completa: " + label + "\nRecortada: " + shortLabel + "\nOcultar: " + hideLabel;
        trayHint.Content = "Viso\n" + CaptureHint.Text;
    }
    private void DismissModeChanged(object sender, RoutedEventArgs e)
    {
        if (DismissTimePanel == null || DismissHelp == null) return;
        bool timed = DismissTimeChoice.IsChecked == true;
        DismissTimePanel.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        DismissHelp.Text = timed ? "Se oculta después del tiempo indicado." : DismissClickChoice.IsChecked == true ? "Se oculta al hacer clic en cualquier parte." : "Se oculta al mover la rueda del mouse.";
    }
    private void CaptureOptionChanged(object sender, SelectionChangedEventArgs e) => UpdateCaptureOptions();
    private void AppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (!configReady) return;
        Appearance.Apply(DarkModeSwitch.IsChecked == true ? "dark" : "light");
        AppearanceIcon.Text = Appearance.IsDark ? "☾" : "☀";
        ApplyNativeAppearance();
        StatusText.Text = "Sin guardar.";
    }
    private void UpdateCaptureOptions()
    {
        if (!configReady) return;
        SelectAreaButton.IsEnabled = !busy && AreaBox.SelectedIndex == 1;
        if (AreaBox.SelectedIndex == 0) { AreaSummary.Text = ""; return; }
        if (settings.SavedArea == null) { AreaSummary.Text = "No hay un área guardada."; return; }
        try
        {
            var screen = SelectedScreen();
            var bounds = settings.SavedArea.Resolve(screen.DeviceName, screen.Bounds);
            AreaSummary.Text = $"Área: {bounds.Width} × {bounds.Height} px";
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
            answer.Hide();
            await HideWindowAnimatedAsync();
            await Task.Delay(300);
            var selector = new RegionWindow(ScreenCapture.Take(screen.Bounds));
            if (selector.ShowDialog() != true || selector.Selected == null) { StatusText.Text = "Selección cancelada."; return; }
            settings.SavedArea = CaptureArea.Create(screen.DeviceName, screen.Bounds, selector.Selected.Value);
            settings.Region = true;
            AreaBox.SelectedIndex = 1;
            settings.Save(KeyBox.Password.Trim(), GeminiKeyBox.Password.Trim());
            StatusText.Text = "Área guardada.";
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
        string savedModel = activeProvider == "Gemini" ? settings.GeminiModel : settings.Model;
        // Keep an existing saved model available without allowing free typing.
        if (!models.Contains(savedModel)) ModelBox.Items.Add(savedModel);
        ModelBox.SelectedItem = savedModel;
        ModelHelp.Text = activeProvider == "Gemini"
            ? "Flash-Lite cuesta menos; Flash ofrece mayor capacidad."
            : "Mini cuesta menos; GPT-5.4 ofrece mayor capacidad.";
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
        bool wasHidden = !IsVisible;
        RestoreWindowVisuals(); Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (wasHidden) AnimateWindowOpen();
        Activate();
    }
    private async Task CaptureAsync(bool shortAnswer = false)
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
            int maxCharacters = settings.MaxResponseCharacters;
            var screen = SelectedScreen();
            var captureBounds = settings.Region
                ? (settings.SavedArea ?? throw new ArgumentException("Elige un área antes de capturar.")).Resolve(screen.DeviceName, screen.Bounds)
                : screen.Bounds;
            SetBusy(true);
            answer.Hide();
            await HideWindowAnimatedAsync();
            await Task.Delay(300);
            var next = ScreenCapture.Take(captureBounds);
            capture = next;
            StatusText.Text = $"Captura lista: {next.Bounds.Width} × {next.Bounds.Height}.";
            captured = true;
            SetBusy(false);
            await SendAsync(shortAnswer, maxCharacters);
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
    private async Task SendAsync(bool shortAnswer, int maxCharacters)
    {
        if (busy || capture == null || exiting) return;
        if (string.IsNullOrWhiteSpace(CurrentKey))
        {
            SelectSection(2);
            StatusText.Text = $"Añade tu API key de {activeProvider}.";
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
            StatusText.Text = "Consultando…";
            answer.Hide();
            var timer = Stopwatch.StartNew();
            var result = await vision.SolveAsync(png, key, model, task, instructions, requestCancellation.Token, provider);
            if (exiting) return;
            timer.Stop();
            ReadSettings(false);
            string displayed = shortAnswer ? ResponseDisplay.Limit(result.Text, maxCharacters) : result.Text;
            answer.Present(displayed, model, result.Usage + $" · {timer.Elapsed.TotalSeconds:F1} s", settings, answerScreen);
            StatusText.Text = "Respuesta lista.";
            hasResponse = true;
        }
        catch (OperationCanceledException)
        {
            if (exiting) return;
            string text = requestCancellation?.IsCancellationRequested == true ? "Consulta cancelada." : "Se agotó el tiempo de espera. Inténtalo de nuevo.";
            StatusText.Text = text;
            answer.Present(text, CurrentModel, "Consulta detenida", settings, answerScreen ?? SelectedScreen());
            hasResponse = true;
        }
        catch (Exception ex)
        {
            if (exiting) return;
            string text = ex is HttpRequestException ? "No se pudo conectar. Revisa tu conexión." : ex.Message;
            StatusText.Text = "No se pudo obtener la respuesta.";
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
        var previous = (settings.HotkeyModifiers, settings.HotkeyVirtualKey, settings.ShortHotkeyModifiers, settings.ShortHotkeyVirtualKey, settings.HideHotkeyModifiers, settings.HideHotkeyVirtualKey, settings.MaxResponseCharacters, settings.DismissMode, settings.DismissSeconds);
        bool changedHotkeys = false;
        try
        {
            ReadSettings(false);
            string error = "No se pudieron registrar los atajos.";
            if (hotkeys == null || !hotkeys.TryReplace(draftModifiers, draftVirtualKey, draftShortModifiers, draftShortVirtualKey, draftHideModifiers, draftHideVirtualKey, out error))
            {
                settings.MaxResponseCharacters = previous.MaxResponseCharacters;
                settings.DismissMode = previous.DismissMode; settings.DismissSeconds = previous.DismissSeconds;
                StatusText.Text = error; UpdateHotkeyDisplay(); return;
            }
            changedHotkeys = true;
            settings.HotkeyModifiers = draftModifiers; settings.HotkeyVirtualKey = draftVirtualKey;
            settings.ShortHotkeyModifiers = draftShortModifiers; settings.ShortHotkeyVirtualKey = draftShortVirtualKey;
            settings.HideHotkeyModifiers = draftHideModifiers; settings.HideHotkeyVirtualKey = draftHideVirtualKey;
            ReadSettings(true);
            UpdateHotkeyDisplay(); UpdateCaptureOptions();
            answer.ApplySettings(settings, SelectedScreen());
            StatusText.Text = "Guardado.";
        }
        catch (Exception ex)
        {
            settings.HotkeyModifiers = previous.HotkeyModifiers; settings.HotkeyVirtualKey = previous.HotkeyVirtualKey;
            settings.ShortHotkeyModifiers = previous.ShortHotkeyModifiers; settings.ShortHotkeyVirtualKey = previous.ShortHotkeyVirtualKey;
            settings.MaxResponseCharacters = previous.MaxResponseCharacters;
            settings.HideHotkeyModifiers = previous.HideHotkeyModifiers; settings.HideHotkeyVirtualKey = previous.HideHotkeyVirtualKey;
            settings.DismissMode = previous.DismissMode; settings.DismissSeconds = previous.DismissSeconds;
            if (changedHotkeys && hotkeys != null && !hotkeys.TryReplace(previous.HotkeyModifiers, previous.HotkeyVirtualKey, previous.ShortHotkeyModifiers, previous.ShortHotkeyVirtualKey, previous.HideHotkeyModifiers, previous.HideHotkeyVirtualKey, out _)) hotkeys.Dispose();
            UpdateHotkeyDisplay(); StatusText.Text = "No se pudo guardar: " + ex.Message;
        }
    }
    private async void TrayClick(object sender, RoutedEventArgs e) => await HideWindowAnimatedAsync();
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
        if (FindName("AppearanceBox") != null || FindName("DarkModeSwitch") is not CheckBox)
            throw new InvalidOperationException("La apariencia debe usar un interruptor en la barra lateral.");
        if (TaskBox.Items.Count != 2) throw new InvalidOperationException("Solo deben existir dos tareas.");
        var initialFull = hotkeys?.Full; var initialShort = hotkeys?.Short;
        bool shortWasRegistered = initialShort?.Registered == true;
        MaxCharactersBox.Text = "0";
        SaveClick(this, new RoutedEventArgs());
        if (hotkeys?.Full != initialFull || hotkeys?.Short != initialShort || (initialShort?.Registered == true) != shortWasRegistered)
            throw new InvalidOperationException("Una longitud inválida cambió los atajos existentes.");
        MaxCharactersBox.Text = "10";
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
        ModelBox.SelectedItem = "gpt-5.4";
        ProviderBox.SelectedIndex = 1;
        if (ModelBox.Items.Count != 4 || !CurrentModel.StartsWith("gemini-")) throw new InvalidOperationException("Modelos de Gemini incorrectos.");
        ModelBox.SelectedItem = "gemini-3.1-flash-lite";
        ProviderBox.SelectedIndex = 0;
        if (CurrentModel != "gpt-5.4") throw new InvalidOperationException("Se perdió el modelo OpenAI al cambiar de proveedor.");
        ProviderBox.SelectedIndex = 1;
        if (CurrentModel != "gemini-3.1-flash-lite") throw new InvalidOperationException("Se perdió el modelo Gemini al cambiar de proveedor.");
        ModelBox.ApplyTemplate();
        if (ModelBox.IsEditable || ModelBox.Template.FindName("PART_EditableTextBox", ModelBox) is not TextBox modelInput || modelInput.Visibility == Visibility.Visible)
            throw new InvalidOperationException("El modelo debe elegirse de la lista, sin campo de escritura.");
        if (ModelBox.SelectedItem is not string chosenModel || chosenModel != CurrentModel)
            throw new InvalidOperationException("La consulta no usa el modelo seleccionado.");
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
            MaxCharactersBox.Text = "1";
            visionTestHandler.Reset();
            flow = CaptureAsync(true);
            await visionTestHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await CaptureAsync(false);
            visionTestHandler.Complete("c. A.5.7");
            await flow;
            if (IsVisible || answer.ResponseText != "c" || visionTestHandler.RequestCount != 3)
                throw new InvalidOperationException("El modo recortado no respetó el límite o duplicó la consulta.");
            visionTestHandler.Reset();
            flow = CaptureAsync();
            await visionTestHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            visionTestHandler.Complete("c. A.5.7");
            await flow;
            if (answer.ResponseText != "c. A.5.7" || visionTestHandler.RequestCount != 4)
                throw new InvalidOperationException("El límite recortó también el modo completo.");
            if (visionTestHandler.Prompts.Distinct().Count() != 1)
                throw new InvalidOperationException("El modo recortado alteró el prompt del modelo.");
            MaxCharactersBox.Text = "10";
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
        using (var firstWindow = new HwndSource(new HwndSourceParameters("VisoShortcutTest1") { ParentWindow = new IntPtr(-3) }))
        using (var secondWindow = new HwndSource(new HwndSourceParameters("VisoShortcutTest2") { ParentWindow = new IntPtr(-3) }))
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
            using var pair = new CaptureHotkeys(firstWindow.Handle);
            int full = 0, shortened = 0;
            for (int candidate = 0x7C; candidate < 0x87 && full == 0; candidate++)
                for (int other = candidate + 1; other <= 0x87; other++)
                    if (pair.TryReplace(7, candidate, 7, other, out _)) { full = candidate; shortened = other; break; }
            if (full == 0 || pair.Full!.Id == pair.Short!.Id) throw new InvalidOperationException("No se registraron dos atajos con identificadores distintos en una ventana.");
            if (!pair.TryReplace(7, shortened, 7, full, out _)) throw new InvalidOperationException("No se pueden intercambiar los dos atajos.");
            var oldFull = pair.Full; var oldShort = pair.Short;
            if (pair.TryReplace(7, full, 7, full, out _)) throw new InvalidOperationException("Se aceptaron dos combinaciones iguales.");
            using var externalKey = new GlobalHotkey(secondWindow.Handle, 0x4500);
            int occupied = 0;
            for (int candidate = 0x7C; candidate <= 0x87; candidate++)
                if (externalKey.TryReplace(7, candidate, out _)) { occupied = candidate; break; }
            if (occupied == 0 || pair.TryReplace(7, shortened, 7, occupied, out _) || pair.Full != oldFull || pair.Short != oldShort || !oldFull!.Registered || !oldShort!.Registered)
                throw new InvalidOperationException("Un conflicto sustituyó alguno de los dos atajos activos.");
            externalKey.Dispose();
            int hidden = 0;
            for (int candidate = 0x7C; candidate <= 0x87; candidate++)
                if (pair.TryReplace(7, pair.Full!.VirtualKey, 7, pair.Short!.VirtualKey, 7, candidate, out _)) { hidden = candidate; break; }
            if (hidden == 0 || pair.Hide?.Registered != true) throw new InvalidOperationException("No se registró el tercer atajo.");
            int fullKey = pair.Full!.VirtualKey, shortKey = pair.Short!.VirtualKey;
            if (!pair.TryReplace(7, shortKey, 7, hidden, 7, fullKey, out _)) throw new InvalidOperationException("No se pueden intercambiar los tres atajos.");
            var beforeFull = pair.Full; var beforeShort = pair.Short; var beforeHide = pair.Hide;
            if (pair.TryReplace(7, shortKey, 7, hidden, 7, hidden, out _)) throw new InvalidOperationException("Se aceptó un atajo de ocultar duplicado.");
            for (int candidate = 0x7C; candidate <= 0x87; candidate++)
                if (externalKey.TryReplace(7, candidate, out _)) { occupied = candidate; break; }
            if (pair.TryReplace(7, shortKey, 7, hidden, 7, occupied, out _) || pair.Full != beforeFull || pair.Short != beforeShort || pair.Hide != beforeHide || beforeHide?.Registered != true)
                throw new InvalidOperationException("Un conflicto al ocultar eliminó uno de los tres atajos.");
        }
        ShowMain();
        await Task.Delay(250);
        ResponseNav.IsChecked = true;
        if (!TextColorSpectrum.IsVisible || !TextColorControls.IsEnabled || PaneTitle.Text != "Apariencia")
            throw new InvalidOperationException("El espectro debe estar disponible directamente en Apariencia.");
        if (!GeneralPanel.IsAncestorOf(DismissTimeChoice) || !CaptureOptionsPanel.IsAncestorOf(HideHotkeyBox))
            throw new InvalidOperationException("Los ajustes de comportamiento y atajos no están en sus secciones.");
        TextColorSpectrum.SelectedColor = System.Windows.Media.Colors.Red;
        ColorBrightness.Value = 0.5;
        if (DefaultTextColorCheck.IsChecked == true || RedBox.Text != "128" || GreenBox.Text != "0" || BlueBox.Text != "0")
            throw new InvalidOperationException("El selector integrado no actualiza RGB y color personalizado.");
        TextColorSpectrum.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        if (GreenBox.Text == "0") throw new InvalidOperationException("El espectro no permite seleccionar con el teclado.");
        DefaultTextColorCheck.IsChecked = false;
        RedBox.Text = "12"; GreenBox.Text = "130"; BlueBox.Text = "240";
        CornerBox.SelectedIndex = 1;
        FontBox.SelectedIndex = 3;
        if (((System.Windows.Media.SolidColorBrush)ResponsePreviewText.Foreground).Color.ToString() != "#FF0C82F0" || ResponsePreviewText.FontSize != 20 || ResponsePreviewText.HorizontalAlignment != HorizontalAlignment.Left)
            throw new InvalidOperationException("La vista previa no refleja el color RGB, tamaño y esquina.");
        ReadSettings(false);
        answer.ApplySettings(settings, screen);
        if (((System.Windows.Media.SolidColorBrush)answer.AnswerText.Foreground).Color.ToString() != "#FF0C82F0")
            throw new InvalidOperationException("La respuesta real no usa el color personalizado.");
        RedBox.Text = "999";
        bool rejectedColor = false;
        try { ReadSettings(false); } catch (ArgumentException) { rejectedColor = true; }
        if (!rejectedColor || ((System.Windows.Media.SolidColorBrush)ResponsePreviewText.Foreground).Color.ToString() != "#FF0C82F0")
            throw new InvalidOperationException("El RGB inválido no fue rechazado o alteró la vista previa.");
        RedBox.Text = "12";
        PreviewShortCheck.IsChecked = true;
        MaxCharactersBox.Text = "3";
        if (ResponsePreviewText.Text != ResponseDisplay.Limit(TaskBox.SelectedIndex == 1 ? "La respuesta es: fotosíntesis." : "B. La fotosíntesis transforma la energía luminosa en energía química.", 3))
            throw new InvalidOperationException("La vista previa no respeta el recorte.");
        PreviewShortCheck.IsChecked = false;
        MaxCharactersBox.Text = "10";
        DefaultTextColorCheck.IsChecked = true;
        CornerBox.SelectedIndex = 0;
        FontBox.SelectedIndex = 1;
        ReadSettings(false);
        answer.ApplySettings(settings, screen);
        if (settings.TextColor != null || ((System.Windows.Media.SolidColorBrush)answer.AnswerText.Foreground).Color != System.Windows.Media.Colors.White)
            throw new InvalidOperationException("No se restauró el color predeterminado.");
        DismissTimeChoice.IsChecked = true;
        if (DismissTimePanel.Visibility != Visibility.Visible) throw new InvalidOperationException("No se mostró el tiempo configurable.");
        DismissScrollChoice.IsChecked = true;
        if (DismissTimePanel.Visibility != Visibility.Collapsed) throw new InvalidOperationException("El tiempo siguió visible en modo scroll.");
        DismissTimeChoice.IsChecked = true;
        await Task.Delay(150);
        UiSnapshot.Save(this, "smoke-response-settings.png");
        settings.DismissMode = ResponseDismissal.Time;
        settings.DismissSeconds = 1;
        answer.Present("Timer test", "", "", settings, screen);
        if (!answer.Dismissal.TimerActive || answer.Dismissal.MouseActive) throw new InvalidOperationException("El modo tiempo activó el mouse.");
        await Task.Delay(700);
        answer.Present("New response", "", "", settings, screen);
        await Task.Delay(500);
        if (!answer.IsVisible) throw new InvalidOperationException("El temporizador anterior ocultó la respuesta nueva.");
        await Task.Delay(650);
        if (answer.IsVisible || answer.Dismissal.TimerActive || answer.Dismissal.MouseActive) throw new InvalidOperationException("El temporizador no ocultó y liberó la respuesta.");
        settings.DismissMode = ResponseDismissal.Scroll;
        answer.Present("Scroll test", "", "", settings, screen);
        if (!answer.Dismissal.MouseActive || answer.Dismissal.TimerActive) throw new InvalidOperationException("El modo scroll no registró el mouse.");
        answer.Dismissal.ProcessMouseInput(0x0001);
        await Task.Delay(100);
        if (!answer.IsVisible) throw new InvalidOperationException("Un clic ocultó la respuesta en modo scroll.");
        answer.Dismissal.ProcessMouseInput(0x0400);
        await Task.Delay(100);
        if (answer.IsVisible || answer.Dismissal.MouseActive) throw new InvalidOperationException("El scroll no ocultó y liberó la respuesta.");
        settings.DismissMode = ResponseDismissal.Click;
        answer.Present("Click test", "", "", settings, screen);
        answer.Dismissal.ProcessMouseInput(0x0400);
        await Task.Delay(100);
        if (!answer.IsVisible) throw new InvalidOperationException("El scroll ocultó la respuesta en modo clic.");
        answer.Dismissal.ProcessMouseInput(0x0001);
        answer.Present("Replacement before queued click", "", "", settings, screen);
        await Task.Delay(100);
        if (!answer.IsVisible) throw new InvalidOperationException("Un clic pendiente ocultó una respuesta nueva.");
        answer.Dismissal.ProcessMouseInput(0x0004);
        await Task.Delay(100);
        if (answer.IsVisible || answer.Dismissal.MouseActive) throw new InvalidOperationException("El clic no ocultó y liberó la respuesta.");
        foreach (string mode in new[] { ResponseDismissal.Time, ResponseDismissal.Click, ResponseDismissal.Scroll })
        {
            settings.DismissMode = mode;
            answer.Present("Hide shortcut test", "", "", settings, screen);
            if (hotkeys?.Hide?.Registered != true) throw new InvalidOperationException("El atajo para ocultar no está disponible.");
            bool handled = false;
            WindowHook(handle, 0x0312, new IntPtr(hotkeys.Hide.Id), IntPtr.Zero, ref handled);
            if (!handled || answer.IsVisible || answer.Dismissal.MouseActive || answer.Dismissal.TimerActive) throw new InvalidOperationException("El atajo no ocultó la respuesta en todos los modos.");
        }
        settings.DismissMode = ResponseDismissal.Time;
        settings.DismissSeconds = 10;
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
        var hiding = HideWindowAnimatedAsync();
        await Task.Delay(40); ShowMain();
        await hiding; await Task.Delay(250);
        if (!IsVisible || Opacity < 0.99) throw new InvalidOperationException("Reabrir durante la transición dejó la ventana oculta.");
        GeneralNav.IsChecked = true;
        await Task.Delay(250);
        UiSnapshot.Save(this, "smoke-light.png");
        DarkModeSwitch.IsChecked = true;
        ReadSettings(false);
        await Task.Delay(100);
        var themeBackground = (System.Windows.Media.SolidColorBrush)WindowSurface.Background;
        var themePanel = (System.Windows.Media.SolidColorBrush)GeneralPanel.Background;
        if (!Appearance.IsDark || settings.Appearance != "dark" || themeBackground.Color.ToString() != "#FF1E1E20" || themePanel.Color.ToString() != "#FF2C2C2E")
            throw new InvalidOperationException("El modo oscuro no actualizó las superficies existentes.");
        if (System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!.Appearance != "dark")
            throw new InvalidOperationException("La apariencia no se conserva en la configuración.");
        UiSnapshot.Save(this, "smoke-dark.png");
        ResponseNav.IsChecked = true;
        await Task.Delay(250);
        UiSnapshot.Save(this, "smoke-appearance-dark.png");
        GeneralNav.IsChecked = true;
        trayMenu.IsOpen = true;
        await Task.Delay(150);
        trayMenu.ApplyTemplate();
        if (!trayMenu.IsOpen || trayMenu.Template == null || tray.ContextMenuStrip != null || tray.Text.Length != 0)
            throw new InvalidOperationException("La bandeja usa una superficie nativa sin el diseño propio.");
        if (((System.Windows.Media.SolidColorBrush)trayMenu.Background).Color.ToString() != "#FF303032")
            throw new InvalidOperationException("El menú de la bandeja no cambió a oscuro.");
        UiSnapshot.Save(trayMenu, "smoke-tray.png");
        trayMenu.IsOpen = false;
        trayHint.IsOpen = true; await Task.Delay(100);
        UiSnapshot.Save(trayHint, "smoke-tooltip.png"); trayHint.IsOpen = false;
        await HideWindowAnimatedAsync();
        trayMenu.IsOpen = true; await Task.Delay(100);
        if (IsVisible || !trayMenu.IsOpen || trayMenu.ActualWidth < 100)
            throw new InvalidOperationException("El menú de la bandeja no funciona con la ventana oculta.");
        ((MenuItem)trayMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(250);
        if (!IsVisible || trayMenu.IsOpen || Opacity < 0.99)
            throw new InvalidOperationException("El menú no reabre la ventana y se cierra correctamente.");
        using (var duplicate = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Appearance.IsDark ? "--smoke-test --dark-smoke" : "--smoke-test") { UseShellExecute = false, CreateNoWindow = true }))
        {
            if (duplicate == null) throw new InvalidOperationException("No se inició la prueba de segunda instancia.");
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (duplicate.ExitCode != 0 || !File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "smoke-duplicate-result.txt")).StartsWith("PASS"))
                throw new InvalidOperationException("Falló el aviso de segunda instancia.");
        }
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
        if (SidebarColumn.Width.Value != 84 || GeneralNavText.Visibility != Visibility.Collapsed || DarkModeSwitch.Visibility != Visibility.Visible || !DarkModeSwitch.IsVisible || AppearanceLabel.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("La ventana compacta no adaptó la barra lateral.");
        CaptureNav.IsChecked = true;
        await Task.Delay(250);
        var compactRender = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        compactRender.Render(this);
        var compactEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        compactEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(compactRender));
        using (var compactFile = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-compact.png"))) compactEncoder.Save(compactFile);
        ResponseNav.IsChecked = true;
        await Task.Delay(250);
        if (TextColorSpectrum.ActualWidth < 100 || !TextColorSpectrum.IsVisible || !RedBox.IsVisible)
            throw new InvalidOperationException("El selector integrado no se adapta a la ventana compacta.");
        UiSnapshot.Save(this, "smoke-appearance-compact.png");
        Width = originalWidth;
        ZoomWindowClick(this, new RoutedEventArgs());
        await Task.Delay(150);
        if (WindowState != WindowState.Maximized) throw new InvalidOperationException("El botón verde no maximiza.");
        ZoomWindowClick(this, new RoutedEventArgs());
        await Task.Delay(150);
        if (WindowState != WindowState.Normal || SidebarColumn.Width.Value != 220) throw new InvalidOperationException("El botón verde no restaura el tamaño.");
        MinimizeWindowClick(this, new RoutedEventArgs());
        await Task.Delay(250);
        if (IsVisible) throw new InvalidOperationException("El botón amarillo no minimiza a la bandeja.");
        ShowMain();
        DarkModeSwitch.IsChecked = false; ReadSettings(false);
        await Task.Delay(100);
        if (Appearance.IsDark || ((System.Windows.Media.SolidColorBrush)WindowSurface.Background).Color.ToString() != "#FFF7F7F9")
            throw new InvalidOperationException("No se pudo volver al modo claro.");
    }
}
