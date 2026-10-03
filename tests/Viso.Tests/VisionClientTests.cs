using System;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Viso.Tests;

public sealed class VisionClientTests
{
    [Fact]
    public void GeminiInternalThoughtsAreExcludedFromDisplayedAnswer()
    {
        const string response = """
            {"candidates":[{"finishReason":"STOP","content":{"parts":[
              {"thought":true,"text":"Internal reasoning"},
              {"text":"C. Respuesta"}
            ]}}]}
            """;
        Assert.Equal("C. Respuesta", VisionClient.ParseGeminiAnswer(response).Text);
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    public void IncompleteOrBlockedGeminiOutputIsRejected(string finishReason)
    {
        string response = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason, content = new { parts = new[] { new { text = "Partial answer" } } } } } });
        Assert.Throws<InvalidOperationException>(() => VisionClient.ParseGeminiAnswer(response));
    }

    [Theory]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"Only reasoning\"}]}}]}")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
    public void GeminiResponseWithoutUsableAnswerIsRejected(string response)
        => Assert.Throws<InvalidOperationException>(() => VisionClient.ParseGeminiAnswer(response));

    [Fact]
    public void ProviderErrorsHideTheApiKey()
    {
        const string key = "fake-key-for-unit-test";
        string body = JsonSerializer.Serialize(new { error = new { message = "Invalid credential: " + key } });
        string message = VisionClient.ErrorMessage(HttpStatusCode.Unauthorized, body, key);
        Assert.DoesNotContain(key, message);
        Assert.Contains("[clave oculta]", message);
    }

    [Fact]
    public void OpenAiRequestPreservesImageAndDisablesStorage()
    {
        byte[] image = { 1, 2, 3, 4 };
        using var payload = JsonDocument.Parse(VisionClient.CreatePayload(image, "custom-model", TaskPrompts.Alternative, ""));
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        var content = payload.RootElement.GetProperty("input")[0].GetProperty("content");
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(image), content[1].GetProperty("image_url").GetString());
    }
}
