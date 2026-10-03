using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace CapturaTutor;

public partial class App : Application
{
    private Mutex? instance;
    private bool showingError;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Array.IndexOf(e.Args, "--evaluate-image") >= 0)
        {
            Shutdown(await ImageEvaluation.RunAsync(e.Args));
            return;
        }
        if (Array.IndexOf(e.Args, "--self-test") >= 0)
        {
            int code = await SelfTest.RunAsync();
            Shutdown(code);
            return;
        }
        bool smokeTest = Array.IndexOf(e.Args, "--smoke-test") >= 0;
        Appearance.Apply(smokeTest ? (Array.IndexOf(e.Args, "--dark-smoke") >= 0 ? "dark" : "light") : Settings.Load(out _).Appearance);
        instance = new Mutex(true, smokeTest ? "Local\\CapturaTutor.SmokeTest" : "Local\\CapturaTutor.Desktop", out bool first);
        if (!first)
        {
            if (smokeTest)
            {
                var alert = new AppleAlert("CapturaTutor ya está abierto", "Puedes abrirlo desde el icono junto al reloj.");
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                string result = "";
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        if (!alert.IsVisible || alert.WindowStyle != WindowStyle.None || !alert.AllowsTransparency || alert.ShowInTaskbar)
                            throw new InvalidOperationException("El aviso de segunda instancia no usa el diálogo propio.");
                        if (!alert.MatchesAppearance()) throw new InvalidOperationException("El aviso no usa la apariencia elegida.");
                        UiSnapshot.Save(alert, "smoke-alert.png"); result = "PASS: aviso real de segunda instancia con diálogo propio y botón Aceptar.";
                    }
                    catch (Exception ex) { result = "FAIL: " + ex; }
                    alert.AcceptForSmokeTest();
                };
                timer.Start(); alert.ShowDialog();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-duplicate-result.txt"), result);
                Shutdown(result.StartsWith("PASS") ? 0 : 1); return;
            }
            AppleAlert.Present("CapturaTutor ya está abierto", "Puedes abrirlo desde el icono junto al reloj.");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (showingError) return;
            showingError = true;
            try { AppleAlert.Present("No se pudo completar la acción", args.Exception.Message, MainWindow); }
            finally { showingError = false; }
        };
        var window = smokeTest ? new MainWindow(new OverlayFlowTestHandler(), false) : new MainWindow();
        MainWindow = window;
        window.Show();
        if (Array.IndexOf(e.Args, "--smoke-test") >= 0)
        {
            try
            {
                await window.SmokeTestAsync();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "PASS: apariencia clara/oscura en vivo y persistente, diálogos y bandeja oscuros, diálogo real de segunda instancia, menú y aviso propios de bandeja (también con ventana oculta), apertura/minimización/reapertura animadas, cancelación de una transición al reabrir, dos atajos y recorte configurable, captura automática sin abrir la ventana principal ni duplicar consultas, respuesta transparente, selector de modelo sin escritura, área guardada, navegación, controles de ventana y vista compacta. Sin API real ni cambios en la configuración.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "FAIL: " + ex);
                Shutdown(1);
            }
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
