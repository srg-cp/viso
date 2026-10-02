using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CapturaTutor;

public sealed class Settings
{
    public string Model { get; set; } = "gpt-5.4-mini";
    public string Corner { get; set; } = "right";
    public string ScreenDevice { get; set; } = "";
    public bool Region { get; set; }
    public bool PreviewBeforeSend { get; set; } = true;
    public bool RememberKey { get; set; }
    public string EncryptedKey { get; set; } = "";
    public string Task { get; set; } = "Resolver paso a paso";
    public string Instructions { get; set; } = "";
    public bool Topmost { get; set; } = true;
    public int FontSize { get; set; } = 16;

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapturaTutor");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Settings Load(out string warning)
    {
        warning = "";
        if (!File.Exists(FilePath)) return new Settings();
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            settings.FontSize = Math.Clamp(settings.FontSize, 12, 24);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            warning = "No se pudo leer la configuración. Vuelve a configurar tu clave y modelo.";
            return new Settings();
        }
    }
    public void Save(string key)
    {
        EncryptedKey = RememberKey && !string.IsNullOrWhiteSpace(key) ? ProtectKey(key) : "";
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
    public string ReadKey() => string.IsNullOrEmpty(EncryptedKey) ? "" : UnprotectKey(EncryptedKey);
    internal static string ProtectKey(string key) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    internal static string UnprotectKey(string encrypted) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));
}
