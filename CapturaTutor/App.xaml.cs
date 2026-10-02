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
        if (Array.IndexOf(e.Args, "--self-test") >= 0)
        {
            int code = await SelfTest.RunAsync();
            Shutdown(code);
            return;
        }
        instance = new Mutex(true, "Local\\CapturaTutor.Desktop", out bool first);
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
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (Array.IndexOf(e.Args, "--smoke-test") >= 0)
        {
            try
            {
                await window.SmokeTestAsync();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-test-result.txt"), "PASS: inicio WPF, captura PNG y panel en ambas esquinas.");
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
