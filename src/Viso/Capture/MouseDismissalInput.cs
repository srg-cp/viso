using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Viso;

internal sealed class MouseDismissalInput : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public uint Type, Size; public IntPtr Device, Parameter; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Mouse { [FieldOffset(4)] public ushort Buttons; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public Header Header; public Mouse Mouse; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr input, uint command, out Input data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern int GetMessageTime();
    private readonly HwndSource source;
    private readonly Action<ushort> onInput;
    private readonly int startedAt = Environment.TickCount;
    private bool registered;
    internal bool IsRegistered => registered;

    public MouseDismissalInput(HwndSource source, Action<ushort> onInput)
    {
        this.source = source;
        this.onInput = onInput;
        source.AddHook(ReadInput);
        var device = new Device { Page = 1, Usage = 2, Flags = 0x100, Window = source.Handle };
        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<Device>()))
        {
            int error = Marshal.GetLastWin32Error();
            source.RemoveHook(ReadInput);
            throw new Win32Exception(error);
        }
        registered = true;
    }

    private IntPtr ReadInput(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (registered && message == 0x00FF && unchecked(GetMessageTime() - startedAt) >= 0)
        {
            uint size = (uint)Marshal.SizeOf<Input>();
            uint read = GetRawInputData(lParam, 0x10000003, out var input, ref size, (uint)Marshal.SizeOf<Header>());
            if (read == size && input.Header.Type == 0 && input.Mouse.Buttons != 0) onInput(input.Mouse.Buttons);
        }
        // Let WPF/DefWindowProc clean up WM_INPUT; never consume normal mouse events.
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!registered) return;
        registered = false;
        var device = new Device { Page = 1, Usage = 2, Flags = 1, Window = IntPtr.Zero };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<Device>());
        source.RemoveHook(ReadInput);
    }
}
