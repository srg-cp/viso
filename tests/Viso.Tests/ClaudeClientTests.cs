using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Viso.Tests;

public sealed class ClaudeClientTests
{
    [Fact]
    public async Task RequestUsesAnthropicEndpointAndIndependentKeyWithExactImage()
    {
        byte[] png = { 1, 2, 3, 4 };
        using var handler = new Handler(async (request, token) =>
        {
            Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("claude-test-key", request.Headers.GetValues("x-api-key").Single());
            Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("x-goog-api-key"));
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = payload.RootElement;
            Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
            Assert.Equal(TaskPrompts.System, root.GetProperty("system").GetString());
            var content = root.GetProperty("messages")[0].GetProperty("content");
            Assert.Equal("image", content[0].GetProperty("type").GetString());
            Assert.Equal("base64", content[0].GetProperty("source").GetProperty("type").GetString());
            Assert.Equal("image/png", content[0].GetProperty("source").GetProperty("media_type").GetString());
            Assert.Equal(Convert.ToBase64String(png), content[0].GetProperty("source").GetProperty("data").GetString());
            Assert.Equal(TaskPrompts.ForTask(TaskPrompts.Complete) + "\nExtra instruction", content[1].GetProperty("text").GetString());
            return Response(HttpStatusCode.OK, """
                {"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"C. Respuesta"}],"usage":{"input_tokens":25,"output_tokens":10}}
                """);
        });
        using var client = new VisionClient(handler);
        var answer = await client.SolveAsync(png, " claude-test-key ", "claude-sonnet-5-5", TaskPrompts.Complete, "Extra instruction", CancellationToken.None, "Claude");
        Assert.Equal("C. Respuesta", answer.Text);
        Assert.Contains("25", answer.Usage);
        Assert.Contains("10", answer.Usage);
    }

    [Theory]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("claude-opus-5-5", true)]
    [InlineData("claude-fable-5-1", true)]
    [InlineData("claude-haiku-4-5", false)]
    public void ThinkingConfigurationMatchesModel(string model, bool adaptive)
    {
        using var payload = JsonDocument.Parse(VisionClient.CreateClaudePayload(new byte[] { 1 }, model, TaskPrompts.Alternative, ""));
        Assert.Equal(16384, payload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(adaptive, payload.RootElement.TryGetProperty("thinking", out var thinking));
        if (adaptive) Assert.Equal("adaptive", thinking.GetProperty("type").GetString());
    }

    [Fact]
    public void ReasoningAndRedactedBlocksAreExcluded()
    {
        var answer = VisionClient.ParseClaudeAnswer("""
            {"stop_reason":"end_turn","content":[{"type":"thinking","thinking":"Secret reasoning"},{"type":"redacted_thinking","data":"hidden"},{"type":"text","text":"C."},{"type":"text","text":"Respuesta"}]}
            """);
        Assert.Equal("C.\n\nRespuesta", answer.Text);
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    [InlineData("tool_use")]
    [InlineData("pause_turn")]
    [InlineData("")]
    public void UnfinishedOrRefusedResponsesAreRejected(string stopReason)
        => Assert.Throws<InvalidOperationException>(() => VisionClient.ParseClaudeAnswer(JsonSerializer.Serialize(new
        { stop_reason = stopReason, content = new[] { new { type = "text", text = "Partial solution" } } })));

    [Theory]
    [InlineData("{\"stop_reason\":\"end_turn\",\"content\":[]}")]
    [InlineData("{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"Only reasoning\"}]}")]
    [InlineData("{\"stop_reason\":\"end_turn\",\"stop_details\":{\"type\":\"refusal\"},\"content\":[{\"type\":\"text\",\"text\":\"Refused\"}]}")]
    [InlineData("{\"type\":\"error\",\"error\":{\"message\":\"Failed\"}}")]
    public void ResponseWithoutUsableSolutionIsRejected(string response)
        => Assert.Throws<InvalidOperationException>(() => VisionClient.ParseClaudeAnswer(response));

    [Fact]
    public void InvalidImagesAndMissingModelAreRejected()
    {
        Assert.Throws<ArgumentException>(() => VisionClient.CreateClaudePayload(Array.Empty<byte>(), "claude-sonnet-5-5", TaskPrompts.Alternative, ""));
        Assert.Throws<ArgumentException>(() => VisionClient.CreateClaudePayload(new byte[7864321], "claude-sonnet-5-5", TaskPrompts.Alternative, ""));
        Assert.Throws<ArgumentException>(() => VisionClient.CreateClaudePayload(new byte[] { 1 }, " ", TaskPrompts.Alternative, ""));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task HttpErrorsDoNotExposeClaudeKey(HttpStatusCode status)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(status, "{\"type\":\"error\",\"error\":{\"message\":\"Invalid claude-test-key\"}}")));
        using var client = new VisionClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SolveAsync(new byte[] { 1 }, " claude-test-key ", "claude-sonnet-5-5", TaskPrompts.Alternative, "", CancellationToken.None, "Claude"));
        Assert.DoesNotContain("claude-test-key", error.Message);
        Assert.Contains("[clave oculta]", error.Message);
    }

    [Fact]
    public async Task CancellationStopsClaudeRequest()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var client = new VisionClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SolveAsync(new byte[] { 1 }, "claude-test-key", "claude-sonnet-5-5", TaskPrompts.Alternative, "", cancellation.Token, "Claude"));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
