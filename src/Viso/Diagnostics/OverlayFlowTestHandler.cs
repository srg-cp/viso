using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Viso;

// Used only by --smoke-test. No external network or real credentials.
internal sealed class OverlayFlowTestHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public List<string> Prompts { get; } = new();
    public List<(string Url, string Model, string Key)> Targets { get; } = new();
    public TaskCompletionSource<bool> Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Complete(string text) => result.SetResult(text);
    public void Fail(System.Exception exception) => result.SetException(exception);
    public void Reset()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        bool gemini = request.RequestUri!.Host == "generativelanguage.googleapis.com";
        bool claude = request.RequestUri.Host == "api.anthropic.com";
        string model = gemini ? request.RequestUri.AbsolutePath.Split('/')[3].Split(':')[0] : payload.RootElement.GetProperty("model").GetString()!;
        string key = gemini || claude ? System.Linq.Enumerable.Single(request.Headers.GetValues(gemini ? "x-goog-api-key" : "x-api-key")) : request.Headers.Authorization!.Parameter!;
        Targets.Add((request.RequestUri.ToString(), model, key));
        Prompts.Add(gemini
            ? payload.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString() + "\n" + payload.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()
            : claude ? payload.RootElement.GetProperty("system").GetString() + "\n" + payload.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("text").GetString()
            : payload.RootElement.GetProperty("instructions").GetString() + "\n" + payload.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Entered.SetResult(true);
        string text = await result.Task.WaitAsync(cancellationToken);
        if (claude) return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                type = "message", stop_reason = "end_turn",
                content = new[] { new { type = "text", text } }
            }), Encoding.UTF8, "application/json")
        };
        if (gemini) return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } }
            }), Encoding.UTF8, "application/json")
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                status = "completed",
                output = new[] { new { type = "message", content = new[] { new { type = "output_text", text } } } }
            }), Encoding.UTF8, "application/json")
        };
    }
}
