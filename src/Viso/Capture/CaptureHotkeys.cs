using System;
using System.Collections.Generic;
using System.Linq;

namespace Viso;

internal sealed class CaptureHotkeys : IDisposable
{
    private readonly IntPtr window;
    private int nextId = 0x4400;
    public GlobalHotkey? Full { get; private set; }
    public GlobalHotkey? Short { get; private set; }
    public GlobalHotkey? Hide { get; private set; }
    public CaptureHotkeys(IntPtr window) => this.window = window;
    public void Initialize(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, uint hideModifiers, int hideKey, out string error)
    {
        Full = new GlobalHotkey(window, nextId); nextId += 2;
        Short = new GlobalHotkey(window, nextId); nextId += 2;
        Hide = new GlobalHotkey(window, nextId); nextId += 2;
        Full.TryReplace(fullModifiers, fullKey, out string fullError);
        Short.TryReplace(shortModifiers, shortKey, out string shortError);
        Hide.TryReplace(hideModifiers, hideKey, out string hideError);
        error = string.Join(" ", new[] { fullError, shortError, hideError }.Where(value => value.Length > 0));
    }
    public bool TryReplace(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, out string error)
        => TryReplaceCore(fullModifiers, fullKey, shortModifiers, shortKey, null, out error);
    public bool TryReplace(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, uint hideModifiers, int hideKey, out string error)
        => TryReplaceCore(fullModifiers, fullKey, shortModifiers, shortKey, (hideModifiers, hideKey), out error);
    private bool TryReplaceCore(uint fullModifiers, int fullKey, uint shortModifiers, int shortKey, (uint Modifiers, int Key)? hide, out string error)
    {
        error = "";
        var combinations = new List<(uint Modifiers, int Key)> { (fullModifiers, fullKey), (shortModifiers, shortKey) };
        if (hide.HasValue) combinations.Add(hide.Value);
        if (combinations.Any(item => !GlobalHotkey.IsValid(item.Modifiers, item.Key)))
        { error = "Cada atajo necesita un modificador y otra tecla."; return false; }
        if (combinations.Distinct().Count() != combinations.Count)
        { error = "Usa una combinación distinta para cada atajo."; return false; }
        var previous = new[] { Full, Short, Hide }.Where(key => key != null).Cast<GlobalHotkey>().ToArray();
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
        var hideAnswer = full == null || shortAnswer == null || !hide.HasValue ? null : Prepare(hide.Value.Modifiers, hide.Value.Key, out error);
        if (full == null || shortAnswer == null || (hide.HasValue && hideAnswer == null))
        {
            foreach (var key in created) key.Dispose();
            return false;
        }
        Full = full; Short = shortAnswer; Hide = hideAnswer;
        foreach (var key in previous) if (key != full && key != shortAnswer && key != hideAnswer) key.Dispose();
        return true;
    }
    public void Dispose() { Full?.Dispose(); Short?.Dispose(); Hide?.Dispose(); }
}
