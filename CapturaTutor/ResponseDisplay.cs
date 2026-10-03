using System;
using System.Globalization;

namespace CapturaTutor;

internal static class ResponseDisplay
{
    public static string Limit(string text, int maxCharacters)
    {
        if (maxCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        // Count visible characters without splitting surrogate pairs or combining accents.
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= maxCharacters ? text : text[..starts[maxCharacters]];
    }
}
