using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Xunit;

namespace Viso.Tests;

public sealed class ResponseTextStyleTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("#0c82f0", "#0C82F0")]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("red", null)]
    [InlineData("#FF123456", null)]
    [InlineData("#12345Z", null)]
    public void ColorSurvivesReloadWithSafeDefaults(string? storedColor, string? expected)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Viso-color-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(new Settings { TextColor = storedColor }));
            var restored = Settings.LoadFromFolders(folder, Path.Combine(folder, "previous"), out _);
            Assert.Equal(expected, restored.TextColor);
            if (expected == null) Assert.Equal(Colors.White, ResponseTextStyle.ColorFor(restored.TextColor));
        }
        finally { Directory.Delete(folder, true); }
    }
}
