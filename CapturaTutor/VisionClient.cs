using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CapturaTutor;

public sealed record Answer(string Text, string Usage);

public sealed class VisionClient : IDisposable
{
    private readonly HttpClient client;
    public VisionClient(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(5);
    }
    internal static string CreatePayload(byte[] image, string model, string task, string instructions)
    {
        if (image.Length == 0) throw new ArgumentException("La captura está vacía.");
        if (image.Length > 20 * 1024 * 1024) throw new ArgumentException("La captura supera 20 MB. Selecciona solo el ejercicio.");
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Selecciona un modelo.");
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["store"] = false,
            ["max_output_tokens"] = 4096,
            ["instructions"] = TaskPrompts.System,
            ["input"] = new object[] { new { role = "user", content = new object[] {
                new { type = "input_text", text = TaskPrompts.ForTask(task) + (string.IsNullOrWhiteSpace(instructions) ? "" : "\n" + instructions) },
                new { type = "input_image", image_url = "data:image/png;base64," + Convert.ToBase64String(image), detail = "high" }
            } } }
        };
        if (model is "gpt-5.4" or "gpt-5.4-mini") payload["reasoning"] = new { effort = "low" };
        return JsonSerializer.Serialize(payload);
    }
    public async Task<Answer> SolveAsync(byte[] image, string key, string model, string task, string instructions, CancellationToken token, string provider = "OpenAI")
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Añade tu API key.");
        if (provider == "Gemini") return await SolveGeminiAsync(image, key, model, task, token);
        if (provider != "OpenAI") throw new ArgumentException("Proveedor no reconocido.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Content = new StringContent(CreatePayload(image, model, task, instructions), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ErrorMessage(response.StatusCode, body, key));
        return ParseAnswer(body);
    }
    internal static string ErrorMessage(HttpStatusCode status, string body, string key, string provider = "OpenAI")
    {
        string detail = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)) detail = message.GetString() ?? "";
        }
        catch (JsonException) { }
        if (!string.IsNullOrEmpty(key)) detail = detail.Replace(key, "[clave oculta]", StringComparison.Ordinal);
        if (detail.Length > 450) detail = detail[..450];
        string friendly = status switch
        {
            HttpStatusCode.Unauthorized => "API key inválida. Revisa la clave.",
            HttpStatusCode.Forbidden => "Tu cuenta no tiene acceso a este modelo. Revisa la clave y los permisos.",
            HttpStatusCode.NotFound => "Modelo no disponible. Elige otro.",
            HttpStatusCode.TooManyRequests => "Límite de uso o saldo insuficiente. Revisa tu cuenta.",
            HttpStatusCode.BadRequest => provider == "Gemini" ? "Gemini rechazó la consulta. Revisa la clave o elige otro modelo." : "El proveedor rechazó la consulta. Elige otro modelo.",
            _ => $"Error del proveedor ({(int)status}). Inténtalo de nuevo."
        };
        return friendly + (string.IsNullOrWhiteSpace(detail) ? "" : "\n\nDetalle: " + detail);
    }
    internal static string CreateGeminiPayload(byte[] image, string task, string model = "gemini-3.5-flash-lite")
    {
        // The inline request limit includes Base64 overhead and the rest of the JSON body.
        if (image.Length == 0 || image.Length > 14 * 1024 * 1024)
            throw new ArgumentException("La captura está vacía o supera los 14 MB.");
        var generation = new Dictionary<string, object> { ["maxOutputTokens"] = 16384 };
        string id = model.StartsWith("models/", StringComparison.Ordinal) ? model[7..] : model;
        // Output brevity is independent of internal reasoning. Do not leave Lite at minimal/off.
        if (id is "gemini-3.5-flash-lite" or "gemini-3.1-flash-lite" or "gemini-3.5-flash")
        {
            generation["thinkingConfig"] = new { thinkingLevel = "high" };
            generation["mediaResolution"] = "MEDIA_RESOLUTION_HIGH";
            generation["temperature"] = 1.0;
        }
        else if (id == "gemini-2.5-flash-lite")
        {
            generation["thinkingConfig"] = new { thinkingBudget = 8192 };
            generation["mediaResolution"] = "MEDIA_RESOLUTION_HIGH";
        }
        return JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = TaskPrompts.System } } },
            contents = new[] { new { role = "user", parts = new object[] {
                new { text = TaskPrompts.ForTask(task) },
                new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(image) } }
            } } },
            generationConfig = generation
        });
    }
    private async Task<Answer> SolveGeminiAsync(byte[] image, string key, string model, string task, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Selecciona un modelo de Gemini con visión.");
        string id = model.StartsWith("models/", StringComparison.Ordinal) ? model[7..] : model;
        if (id.Length == 0 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '.' && c != '_'))
            throw new ArgumentException("El identificador del modelo Gemini no es válido.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(id) + ":generateContent");
        request.Headers.Add("x-goog-api-key", key.Trim());
        request.Content = new StringContent(CreateGeminiPayload(image, task, id), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ErrorMessage(response.StatusCode, body, key, "Gemini"));
        return ParseGeminiAnswer(body);
    }
    internal static Answer ParseGeminiAnswer(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var block) && block.GetString() is string reason && reason != "BLOCK_REASON_UNSPECIFIED")
            throw new InvalidOperationException("Gemini no pudo responder a esta captura (" + reason + ").");
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini no devolvió una respuesta. Prueba una región más pequeña u otro modelo.");
        var candidate = candidates[0];
        string finish = candidate.TryGetProperty("finishReason", out var f) ? f.GetString() ?? "" : "";
        if (finish == "MAX_TOKENS")
            throw new InvalidOperationException("El modelo agotó su margen de razonamiento. La respuesta incompleta no se mostrará como solución. Captura un solo ejercicio y vuelve a consultar.");
        if (finish is "SAFETY" or "RECITATION" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "IMAGE_SAFETY")
            throw new InvalidOperationException("Gemini no pudo responder a esta captura (" + finish + ").");
        var parts = new List<string>();
        if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var output))
            foreach (var part in output.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (part.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString())) parts.Add(text.GetString()!);
            }
        if (parts.Count == 0) throw new InvalidOperationException("Gemini no devolvió texto. Prueba otro modelo o captura un solo ejercicio.");
        string answer = string.Join("\n", parts);
        string usage = "";
        if (root.TryGetProperty("usageMetadata", out var u))
        {
            int input = u.TryGetProperty("promptTokenCount", out var i) ? i.GetInt32() : 0;
            int generated = u.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt32() : 0;
            int thinking = u.TryGetProperty("thoughtsTokenCount", out var t) ? t.GetInt32() : 0;
            usage = $"Tokens: {input:N0} entrada · {generated + thinking:N0} salida";
            if (u.TryGetProperty("thoughtsTokenCount", out _)) usage += $" · {thinking:N0} razonamiento";
        }
        return new Answer(answer, usage);
    }
    internal static Answer ParseAnswer(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var parts = new List<string>();
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException("El modelo no pudo responder. Inténtalo de nuevo.");
        if (root.TryGetProperty("output", out var output))
            foreach (var item in output.EnumerateArray())
                if (item.TryGetProperty("content", out var content))
                    foreach (var part in content.EnumerateArray())
                    {
                        string type = part.TryGetProperty("type", out var kind) ? kind.GetString() ?? "" : "";
                        if (type == "output_text" && part.TryGetProperty("text", out var text)) parts.Add(text.GetString() ?? "");
                        if (type == "refusal" && part.TryGetProperty("refusal", out var refusal)) parts.Add(refusal.GetString() ?? "");
                    }
        string answer = string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("El modelo no devolvió texto. Prueba otro modelo.");
        if (root.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
            answer += "\n\n[Respuesta incompleta. Vuelve a consultar.]";
        string usage = "";
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object && u.TryGetProperty("input_tokens", out var i) && u.TryGetProperty("output_tokens", out var o))
            usage = $"Tokens: {i.GetInt32():N0} entrada · {o.GetInt32():N0} salida";
        return new Answer(answer, usage);
    }
    public void Dispose() => client.Dispose();
}
