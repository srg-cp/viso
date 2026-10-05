using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Viso.Tests;

public sealed class ModelShortcutTests
{
    [Fact]
    public void OldSettingsKeepExistingModelAndHotkeys()
    {
        WithSettings("{\"Provider\":\"Gemini\",\"GeminiModel\":\"gemini-2.5-flash-lite\",\"HotkeyModifiers\":3,\"HotkeyVirtualKey\":65}", (settings, warning) =>
        {
            Assert.Empty(settings.ModelShortcuts);
            Assert.Equal("Gemini", settings.Provider);
            Assert.Equal("gemini-2.5-flash-lite", settings.GeminiModel);
            Assert.Equal(3u, settings.HotkeyModifiers);
            Assert.Equal(65, settings.HotkeyVirtualKey);
            Assert.Empty(warning);
        });
    }

    [Fact]
    public void ModelsAndAnswerModesSurviveReload()
    {
        var original = new Settings();
        original.ModelShortcuts.Add(new() { Model = "gpt-5.4", VirtualKey = 0x31 });
        original.ModelShortcuts.Add(new() { Provider = "Gemini", Model = "gemini-3.1-flash-lite", VirtualKey = 0x32, ShortAnswer = true });
        WithSettings(JsonSerializer.Serialize(original), (settings, warning) =>
        {
            Assert.Equal(2, settings.ModelShortcuts.Count);
            Assert.Equal("gpt-5.4", settings.ModelShortcuts[0].Model);
            Assert.Equal("OpenAI", settings.ModelShortcuts[0].Provider);
            Assert.False(settings.ModelShortcuts[0].ShortAnswer);
            Assert.Equal("Gemini", settings.ModelShortcuts[1].Provider);
            Assert.Equal("gemini-3.1-flash-lite", settings.ModelShortcuts[1].Model);
            Assert.True(settings.ModelShortcuts[1].ShortAnswer);
            Assert.Empty(warning);
        });
    }

    [Fact]
    public void InvalidAndConflictingShortcutsDoNotPreventValidOnesLoading()
    {
        var original = new Settings();
        original.ModelShortcuts.Add(new() { VirtualKey = original.HotkeyVirtualKey });
        original.ModelShortcuts.Add(new() { VirtualKey = original.ShortHotkeyVirtualKey });
        original.ModelShortcuts.Add(new() { VirtualKey = original.HideHotkeyVirtualKey });
        original.ModelShortcuts.Add(new() { VirtualKey = 0x31 });
        original.ModelShortcuts.Add(new() { VirtualKey = 0x31, Model = "gpt-5.4" });
        original.ModelShortcuts.Add(new() { Modifiers = 0, VirtualKey = 0x32 });
        original.ModelShortcuts.Add(new() { Provider = "Unknown", VirtualKey = 0x33 });
        original.ModelShortcuts.Add(new() { Model = "", VirtualKey = 0x34 });
        original.ModelShortcuts.Add(null!);
        WithSettings(JsonSerializer.Serialize(original), (settings, warning) =>
        {
            Assert.Single(settings.ModelShortcuts);
            Assert.Equal(0x31, settings.ModelShortcuts[0].VirtualKey);
            Assert.NotEmpty(warning);
        });
    }

    [Fact]
    public void NullShortcutListLoadsAsEmpty()
        => WithSettings("{\"ModelShortcuts\":null}", (settings, _) => Assert.Empty(settings.ModelShortcuts));

    [Fact]
    public void ClaudeProviderModelShortcutsAndEncryptedKeySurviveReload()
    {
        var original = new Settings { Provider = "Claude", ClaudeModel = "claude-haiku-4-5", RememberKey = true,
            EncryptedKey = Settings.ProtectKey("openai-key"), EncryptedGeminiKey = Settings.ProtectKey("gemini-key"),
            EncryptedClaudeKey = Settings.ProtectKey("claude-key") };
        original.ModelShortcuts.Add(new() { Provider = "Claude", Model = "claude-sonnet-5-5", VirtualKey = 0x31, ShortAnswer = true });
        string json = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("claude-key", json);
        WithSettings(json, (settings, warning) =>
        {
            Assert.Equal("Claude", settings.Provider);
            Assert.Equal("claude-haiku-4-5", settings.ClaudeModel);
            Assert.Equal("claude-key", settings.ReadClaudeKey());
            Assert.Equal("openai-key", settings.ReadKey());
            Assert.Equal("gemini-key", settings.ReadGeminiKey());
            Assert.Single(settings.ModelShortcuts);
            Assert.Equal("Claude", settings.ModelShortcuts[0].Provider);
            Assert.True(settings.ModelShortcuts[0].ShortAnswer);
            Assert.Empty(warning);
        });
    }

    private static void WithSettings(string json, Action<Settings, string> verify)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Viso-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"), json);
            var settings = Settings.LoadFromFolders(folder, folder, out string warning);
            verify(settings, warning);
        }
        finally { Directory.Delete(folder, true); }
    }
}
