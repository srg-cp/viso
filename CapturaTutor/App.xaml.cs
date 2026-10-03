using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace CapturaTutor;

public partial class App : Application
{
    private Mutex? instance;
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
        instance = new Mutex(true, smokeTest ? "Local\\CapturaTutor.SmokeTest" : "Local\\CapturaTutor.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("CapturaTutor ya está abierto. Búscalo en la barra de tareas o junto al reloj.", "CapturaTutor");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("No se pudo completar la acción. " + args.Exception.Message, "CapturaTutor", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        var window = smokeTest ? new MainWindow(new OverlayFlowTestHandler(), false) : new MainWindow();
        MainWindow = window;
        window.Show();
        if (Array.IndexOf(e.Args, "--smoke-test") >= 0)
        {
            try
            {
                await window.SmokeTestAsync();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "PASS: ventana con controles propios, maximización/restauración, minimización, navegación y vista compacta, tipo de enunciado, edición de modelo con controles nuevos, captura y envío automáticos sin abrir la ventana principal ni duplicar consultas, respuesta transparente sin activación y con clics a través, ambas esquinas, área guardada y atajos. Sin API real ni cambios en la configuración.");
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
