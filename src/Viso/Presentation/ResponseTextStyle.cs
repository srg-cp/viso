using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Viso;

internal static class ResponseTextStyle
{
    public static string? NormalizeColor(string? value)
        => value is { Length: 7 } && value[0] == '#' &&
           uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? value.ToUpperInvariant() : null;

    public static Color ColorFor(string? value)
        => NormalizeColor(value) is string color ? (Color)ColorConverter.ConvertFromString(color) : Colors.White;

    public static void Apply(TextBlock text, Settings settings)
    {
        text.FontSize = settings.FontSize;
        text.Foreground = new SolidColorBrush(ColorFor(settings.TextColor));
        text.TextAlignment = settings.Corner == "left" ? TextAlignment.Left : TextAlignment.Right;
    }
}
