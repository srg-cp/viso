using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Viso.Tests;

public sealed class ResponseDismissalTests
{
    [Theory]
    [InlineData("click", 0x0001, true)]
    [InlineData("click", 0x0004, true)]
    [InlineData("click", 0x0010, true)]
    [InlineData("click", 0x0040, true)]
    [InlineData("click", 0x0100, true)]
    [InlineData("click", 0x0002, false)]
    [InlineData("click", 0x0400, false)]
    [InlineData("scroll", 0x0400, true)]
    [InlineData("scroll", 0x0800, true)]
    [InlineData("scroll", 0x0001, false)]
    [InlineData("scroll", 0x0004, false)]
    [InlineData("scroll", 0, false)]
    [InlineData("time", 0x0400, false)]
    [InlineData("time", 0x0001, false)]
    public void OnlyTheSelectedMouseActionDismissesTheAnswer(string mode, int buttons, bool expected)
        => Assert.Equal(expected, ResponseDismissal.MatchesMouse(mode, (ushort)buttons));

    [Fact]
    public void OldSettingsGetDismissalDefaults()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{\"MaxResponseCharacters\":12}")!;
        Assert.Equal("time", settings.DismissMode);
        Assert.Equal(10, settings.DismissSeconds);
        Assert.Equal(5u, settings.HideHotkeyModifiers);
        Assert.Equal(0x58, settings.HideHotkeyVirtualKey);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("click")]
    [InlineData("scroll")]
    public void DismissalSettingsAndIndependentShortcutSurviveReload(string mode)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Viso-dismissal-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var stored = new Settings { DismissMode = mode, DismissSeconds = 27, HideHotkeyModifiers = 7, HideHotkeyVirtualKey = 0x51 };
            File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(stored));
            var restored = Settings.LoadFromFolders(folder, Path.Combine(folder, "previous"), out string warning);
            Assert.Empty(warning);
            Assert.Equal(mode, restored.DismissMode);
            Assert.Equal(27, restored.DismissSeconds);
            Assert.Equal(7u, restored.HideHotkeyModifiers);
            Assert.Equal(0x51, restored.HideHotkeyVirtualKey);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void InvalidSettingsAreNormalizedAndHideShortcutAvoidsCaptureShortcuts()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Viso-dismissal-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var stored = new Settings { DismissMode = "invalid", DismissSeconds = 0, HotkeyVirtualKey = 0x58, ShortHotkeyModifiers = 7, ShortHotkeyVirtualKey = 0x58 };
            File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(stored));
            var restored = Settings.LoadFromFolders(folder, Path.Combine(folder, "previous"), out _);
            Assert.Equal("time", restored.DismissMode);
            Assert.Equal(1, restored.DismissSeconds);
            Assert.True(GlobalHotkey.IsValid(restored.HideHotkeyModifiers, restored.HideHotkeyVirtualKey));
            Assert.NotEqual((stored.HotkeyModifiers, stored.HotkeyVirtualKey), (restored.HideHotkeyModifiers, restored.HideHotkeyVirtualKey));
            Assert.NotEqual((stored.ShortHotkeyModifiers, stored.ShortHotkeyVirtualKey), (restored.HideHotkeyModifiers, restored.HideHotkeyVirtualKey));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
