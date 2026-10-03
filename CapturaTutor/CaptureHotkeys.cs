using System;
using System.Collections.Generic;
using System.Linq;

namespace CapturaTutor;

internal sealed class CaptureHotkeys : IDisposable
{
    private readonly IntPtr window;
    private int nextId = 0x4400;
    public GlobalHotkey? Full { get; private set; }
    public GlobalHotkey? Short { get; private set; }
    public CaptureHotkeys(IntPtr window) => this.window = window;
    public void Initialize(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, out string error)
    {
        Full = new GlobalHotkey(window, nextId); nextId += 2;
        Short = new GlobalHotkey(window, nextId); nextId += 2;
        Full.TryReplace(fullModifiers, fullKey, out string fullError);
        Short.TryReplace(shortModifiers, shortKey, out string shortError);
        error = string.Join(" ", new[] { fullError, shortError }.Where(value => value.Length > 0));
    }
    public bool TryReplace(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, out string error)
    {
        error = "";
        if (!GlobalHotkey.IsValid(fullModifiers, fullKey) || !GlobalHotkey.IsValid(shortModifiers, shortKey))
        { error = "Los dos atajos necesitan un modificador y otra tecla."; return false; }
        if (fullModifiers == shortModifiers && fullKey == shortKey)
        { error = "Usa combinaciones distintas para la respuesta completa y la recortada."; return false; }
        var previous = new[] { Full, Short }.Where(key => key != null).Cast<GlobalHotkey>().ToArray();
        var created = new List<GlobalHotkey>();
        GlobalHotkey? Prepare(uint modifiers, int key, out string failure)
        {
            failure = "";
            var existing = previous.FirstOrDefault(item => item.Registered && item.Modifiers == modifiers && item.VirtualKey == key);
            if (existing != null) return existing;
            int id;
            do { id = nextId; nextId = nextId >= 0xBFFC ? 0x4400 : nextId + 2; }
            while (previous.Concat(created).Any(item => item.Id == id || item.Id == id + 1));
            var candidate = new GlobalHotkey(window, id);
            if (!candidate.TryReplace(modifiers, key, out failure)) { candidate.Dispose(); return null; }
            created.Add(candidate);
            return candidate;
        }
        var full = Prepare(fullModifiers, fullKey, out error);
        var shortAnswer = full == null ? null : Prepare(shortModifiers, shortKey, out error);
        if (full == null || shortAnswer == null)
        {
            foreach (var key in created) key.Dispose();
            return false;
        }
        Full = full; Short = shortAnswer;
        foreach (var key in previous) if (key != full && key != shortAnswer) key.Dispose();
        return true;
    }
    public void Dispose() { Full?.Dispose(); Short?.Dispose(); }
}
