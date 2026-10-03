using System;
using Xunit;

namespace Viso.Tests;

public sealed class ResponseDisplayTests
{
    [Theory]
    [InlineData("", 1, "")]
    [InlineData("C. Respuesta", 3, "C. ")]
    [InlineData("Sí", 2, "Sí")]
    [InlineData("Sí", 10, "Sí")]
    [InlineData("a\u0301b", 1, "a\u0301")]
    [InlineData("👩🏽‍💻ABC", 1, "👩🏽‍💻")]
    [InlineData("🇵🇪ABC", 2, "🇵🇪A")]
    public void LimitPreservesVisibleCharacters(string answer, int limit, string expected)
        => Assert.Equal(expected, ResponseDisplay.Limit(answer, limit));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidLimitIsRejected(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ResponseDisplay.Limit("C", limit));
}
