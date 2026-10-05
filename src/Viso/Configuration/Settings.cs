using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Viso;

public sealed class Settings
{
    public string Model { get; set; } = "gpt-5.4-mini";
    public string Provider { get; set; } = "OpenAI";
    public string GeminiModel { get; set; } = "gemini-3.5-flash-lite";
    public string ClaudeModel { get; set; } = "claude-sonnet-5-5";
    public List<ModelShortcut> ModelShortcuts { get; set; } = new();
    public string Corner { get; set; } = "right";
    public string Appearance { get; set; } = "light";
    public string ScreenDevice { get; set; } = "";
    public bool Region { get; set; }
    public CaptureArea? SavedArea { get; set; }
    public uint HotkeyModifiers { get; set; } = 5;
    public int HotkeyVirtualKey { get; set; } = 0x53;
    public uint ShortHotkeyModifiers { get; set; } = 5;
    public int ShortHotkeyVirtualKey { get; set; } = 0x44;
    public int MaxResponseCharacters { get; set; } = 10;
    public uint HideHotkeyModifiers { get; set; } = 5;
    public int HideHotkeyVirtualKey { get; set; } = 0x58;
    public string DismissMode { get; set; } = ResponseDismissal.Time;
    public int DismissSeconds { get; set; } = 10;
    public bool PreviewBeforeSend { get; set; } = false;
    public bool RememberKey { get; set; }
    public string EncryptedKey { get; set; } = "";
    public string EncryptedGeminiKey { get; set; } = "";
    public string EncryptedClaudeKey { get; set; } = "";
    public string Task { get; set; } = TaskPrompts.Alternative;
    public string Instructions { get; set; } = "";
    public bool Topmost { get; set; } = true;
    public int FontSize { get; set; } = 16;
    public string? TextColor { get; set; }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viso");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Settings Load(out string warning)
        => LoadFromFolders(Folder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapturaTutor"), out warning);

    internal static Settings LoadFromFolders(string folder, string previousFolder, out string warning)
    {
        warning = "";
        string path = Path.Combine(folder, "settings.json");
        if (!File.Exists(path))
        {
            // Read existing installations; subsequent saves use the Viso folder.
            path = Path.Combine(previousFolder, "settings.json");
            if (!File.Exists(path)) return new Settings();
        }
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
            settings.FontSize = Math.Clamp(settings.FontSize, 12, 24);
            settings.TextColor = ResponseTextStyle.NormalizeColor(settings.TextColor);
            settings.Appearance = settings.Appearance == "dark" ? "dark" : "light";
            settings.DismissMode = ResponseDismissal.Normalize(settings.DismissMode);
            settings.DismissSeconds = Math.Clamp(settings.DismissSeconds, 1, 3600);
            settings.MaxResponseCharacters = Math.Clamp(settings.MaxResponseCharacters, 1, 10000);
            settings.Provider = settings.Provider is "Gemini" or "Claude" ? settings.Provider : "OpenAI";
            settings.Task = settings.Task == TaskPrompts.Complete || settings.Task == "Completar el enunciado" ? TaskPrompts.Complete : TaskPrompts.Alternative;
            if (!GlobalHotkey.IsValid(settings.HotkeyModifiers, settings.HotkeyVirtualKey))
            {
                settings.HotkeyModifiers = 5;
                settings.HotkeyVirtualKey = 0x53;
                warning = "Atajo restablecido: Alt + Mayús + S.";
            }
            if (!GlobalHotkey.IsValid(settings.ShortHotkeyModifiers, settings.ShortHotkeyVirtualKey) ||
                (settings.ShortHotkeyModifiers == settings.HotkeyModifiers && settings.ShortHotkeyVirtualKey == settings.HotkeyVirtualKey))
            {
                settings.ShortHotkeyModifiers = 5; settings.ShortHotkeyVirtualKey = 0x44;
                if (settings.HotkeyModifiers == 5 && settings.HotkeyVirtualKey == 0x44) settings.ShortHotkeyModifiers = 7;
                warning = "Se restableció el atajo de respuesta recortada.";
            }
            if (!GlobalHotkey.IsValid(settings.HideHotkeyModifiers, settings.HideHotkeyVirtualKey) ||
                (settings.HideHotkeyModifiers == settings.HotkeyModifiers && settings.HideHotkeyVirtualKey == settings.HotkeyVirtualKey) ||
                (settings.HideHotkeyModifiers == settings.ShortHotkeyModifiers && settings.HideHotkeyVirtualKey == settings.ShortHotkeyVirtualKey))
            {
                settings.HideHotkeyVirtualKey = 0x58;
                foreach (uint modifiers in new uint[] { 5, 7, 3 })
                {
                    if ((modifiers == settings.HotkeyModifiers && settings.HotkeyVirtualKey == 0x58) ||
                        (modifiers == settings.ShortHotkeyModifiers && settings.ShortHotkeyVirtualKey == 0x58)) continue;
                    settings.HideHotkeyModifiers = modifiers;
                    break;
                }
                warning = "Se restableció el atajo para ocultar la respuesta.";
            }
            settings.ModelShortcuts ??= new();
            var combinations = new HashSet<(uint, int)>
            {
                (settings.HotkeyModifiers, settings.HotkeyVirtualKey),
                (settings.ShortHotkeyModifiers, settings.ShortHotkeyVirtualKey),
                (settings.HideHotkeyModifiers, settings.HideHotkeyVirtualKey)
            };
            bool omittedShortcuts = false;
            settings.ModelShortcuts.RemoveAll(shortcut =>
            {
                bool invalid = shortcut == null ||
                    (shortcut.Provider != "OpenAI" && shortcut.Provider != "Gemini" && shortcut.Provider != "Claude") ||
                    string.IsNullOrWhiteSpace(shortcut.Model) ||
                    !GlobalHotkey.IsValid(shortcut.Modifiers, shortcut.VirtualKey) ||
                    !combinations.Add((shortcut.Modifiers, shortcut.VirtualKey));
                if (invalid) omittedShortcuts = true;
                return invalid;
            });
            if (omittedShortcuts) warning = "Se omitieron atajos por modelo inválidos o duplicados.";
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            warning = "No se pudo cargar la configuración. Revisa tus ajustes.";
            return new Settings();
        }
    }
    public void Save(string key, string geminiKey = "", string claudeKey = "")
    {
        EncryptedKey = RememberKey && !string.IsNullOrWhiteSpace(key) ? ProtectKey(key) : "";
        EncryptedGeminiKey = RememberKey && !string.IsNullOrWhiteSpace(geminiKey) ? ProtectKey(geminiKey) : "";
        EncryptedClaudeKey = RememberKey && !string.IsNullOrWhiteSpace(claudeKey) ? ProtectKey(claudeKey) : "";
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
    public string ReadKey() => string.IsNullOrEmpty(EncryptedKey) ? "" : UnprotectKey(EncryptedKey);
    public string ReadGeminiKey() => string.IsNullOrEmpty(EncryptedGeminiKey) ? "" : UnprotectKey(EncryptedGeminiKey);
    public string ReadClaudeKey() => string.IsNullOrEmpty(EncryptedClaudeKey) ? "" : UnprotectKey(EncryptedClaudeKey);
    internal static string ProtectKey(string key) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    internal static string UnprotectKey(string encrypted) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
}
