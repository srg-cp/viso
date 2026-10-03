using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Viso;

internal sealed class AnswerDismissal : IDisposable
{
    private readonly Window window;
    private DispatcherTimer? timer;
    private MouseDismissalInput? mouse;
    private string mode = ResponseDismissal.Time;
    private int seconds = 10;
    private int generation;
    private bool disposed;
    internal bool MouseActive => mouse?.IsRegistered == true;
    internal bool TimerActive => timer?.IsEnabled == true;
    public event Action<string>? Warning;

    public AnswerDismissal(Window window)
    {
        this.window = window;
        window.IsVisibleChanged += VisibilityChanged;
    }

    public void Configure(string mode, int seconds)
    {
        this.mode = ResponseDismissal.Normalize(mode);
        this.seconds = Math.Clamp(seconds, 1, 3600);
        Restart();
    }

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Restart();
    private void Restart()
    {
        Stop();
        if (disposed || !window.IsVisible) return;
        int current = generation;
        if (mode != ResponseDismissal.Time)
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source != null)
            {
                try
                {
                    mouse = new MouseDismissalInput(source, ProcessMouseInput);
                    return;
                }
                catch (Win32Exception) { }
            }
            Warning?.Invoke("No se pudo activar el mouse. La respuesta se ocultará por tiempo.");
        }
        timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => HideIfCurrent(current);
        timer.Start();
    }

    private void HideIfCurrent(int current)
    {
        if (!disposed && current == generation && window.IsVisible) window.Hide();
    }

    internal void ProcessMouseInput(ushort buttons)
    {
        if (!MouseActive || !window.IsVisible || !ResponseDismissal.MatchesMouse(mode, buttons)) return;
        int current = generation;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => HideIfCurrent(current)));
    }

    private void Stop()
    {
        generation++;
        timer?.Stop(); timer = null;
        mouse?.Dispose(); mouse = null;
    }

    public void Dispose()
    {
        disposed = true;
        window.IsVisibleChanged -= VisibilityChanged;
        Stop();
    }
}
