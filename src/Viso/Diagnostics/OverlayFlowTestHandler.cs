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
    public TaskCompletionSource<bool> Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Complete(string text) => result.SetResult(text);
    public void Reset()
    {
        Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        Prompts.Add(payload.RootElement.GetProperty("instructions").GetString() + "\n" + payload.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Entered.SetResult(true);
        string text = await result.Task.WaitAsync(cancellationToken);
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
