using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace CapturaTutor;

internal sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private readonly IntPtr window;
    private readonly int firstId;
    public int Id { get; private set; }
    public bool Registered { get; private set; }
    public uint Modifiers { get; private set; }
    public int VirtualKey { get; private set; }
    public GlobalHotkey(IntPtr window, int firstId = 0x4354) { this.window = window; this.firstId = firstId; Id = firstId; }
    public static bool IsValid(uint modifiers, int virtualKey) => modifiers > 0 && (modifiers & ~15u) == 0 && virtualKey >= 0x08 && virtualKey <= 0xFE && virtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C);
    public bool TryReplace(uint modifiers, int virtualKey, out string error)
    {
        error = "";
        if (!IsValid(modifiers, virtualKey)) { error = "Combina Ctrl, Alt, Mayús o Windows con otra tecla."; return false; }
        if (Registered && Modifiers == modifiers && VirtualKey == virtualKey) return true;
        int next = Id == firstId ? firstId + 1 : firstId;
        // Register first. An occupied shortcut must never remove the working shortcut.
        if (!RegisterHotKey(window, next, modifiers | 0x4000, (uint)virtualKey))
        { error = "La combinación " + Label(modifiers, virtualKey) + " no está disponible. Elige otra."; return false; }
        if (Registered) UnregisterHotKey(window, Id);
        Id = next; Registered = true; Modifiers = modifiers; VirtualKey = virtualKey;
        return true;
    }
    public static string Label(uint modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Mayús");
        if ((modifiers & 8) != 0) parts.Add("Windows");
        parts.Add(KeyInterop.KeyFromVirtualKey(virtualKey).ToString());
        return string.Join(" + ", parts);
    }
    public void Dispose()
    {
        if (Registered) UnregisterHotKey(window, Id);
        Registered = false;
    }
}
