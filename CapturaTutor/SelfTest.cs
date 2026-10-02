using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CapturaTutor;

internal static class SelfTest
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\"},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"x = 3\"}]}],\"usage\":{\"input_tokens\":100,\"output_tokens\":50}}";
        public string Payload = "";
        public bool AuthCorrect;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Payload = await request.Content!.ReadAsStringAsync(token);
            AuthCorrect = request.Headers.Authorization?.ToString() == "Bearer local-test-key";
            Check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses", "Endpoint oficial");
            return new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Falló: " + name);
    }
    public static async Task<int> RunAsync()
    {
        string log = "";
        int exit = 0;
        try
        {
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=");
            var handler = new FakeHandler();
            using var client = new VisionClient(handler);
            var answer = await client.SolveAsync(png, "local-test-key", "gpt-5.4-mini", "Resolver", "Solo pregunta 1", CancellationToken.None);
            Check(answer.Text == "x = 3" && answer.Usage.Contains("100"), "Respuesta y consumo");
            Check(handler.AuthCorrect, "Autenticación");
            using var payload = JsonDocument.Parse(handler.Payload);
            var root = payload.RootElement;
            Check(!root.GetProperty("store").GetBoolean(), "store false");
            Check(root.GetProperty("model").GetString() == "gpt-5.4-mini", "Modelo seleccionado");
            var image = root.GetProperty("input")[0].GetProperty("content")[1];
            Check(image.GetProperty("type").GetString() == "input_image", "Entrada visual");
            Check(image.GetProperty("image_url").GetString() == "data:image/png;base64," + Convert.ToBase64String(png), "Imagen exacta");
            Check(root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!.Contains("Solo pregunta 1"), "Instrucciones");
            Check(root.GetProperty("reasoning").GetProperty("effort").GetString() == "medium", "Razonamiento");
            log += "PASS: solicitud autenticada con PNG, modelo, tarea, razonamiento y store=false.\n";
            handler.Status = HttpStatusCode.Unauthorized;
            handler.Body = "{\"error\":{\"message\":\"Invalid local-test-key\"}}";
            try
            {
                await client.SolveAsync(png, "local-test-key", "gpt-5.4", "Resolver", "", CancellationToken.None);
                throw new Exception("No se detectó 401.");
            }
            catch (InvalidOperationException ex)
            {
                Check(ex.Message.Contains("inválida") && !ex.Message.Contains("local-test-key"), "Error y clave oculta");
            }
            handler.Status = HttpStatusCode.TooManyRequests;
            handler.Body = "{\"error\":{\"message\":\"quota\"}}";
            try
            {
                await client.SolveAsync(png, "local-test-key", "gpt-5.4", "Resolver", "", CancellationToken.None);
                throw new Exception("No se detectó 429.");
            }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("saldo"), "Cuota"); }
            log += "PASS: errores de autenticación/cuota y ocultamiento de clave.\n";
            var incomplete = VisionClient.ParseAnswer("{\"status\":\"incomplete\",\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"Parcial\"}]}]}");
            Check(incomplete.Text.Contains("incompleta"), "Respuesta parcial");
            var refusal = VisionClient.ParseAnswer("{\"output\":[{\"content\":[{\"type\":\"refusal\",\"refusal\":\"No disponible\"}]}]}");
            Check(refusal.Text == "No disponible", "Rechazo");
            try { VisionClient.ParseAnswer("{\"output\":[]}"); throw new Exception("No se detectó respuesta vacía."); }
            catch (InvalidOperationException) { }
            log += "PASS: respuestas parciales, rechazo y ausencia de texto.\n";
            string encrypted = Settings.ProtectKey("fake-secret-for-test");
            Check(!encrypted.Contains("fake-secret") && Settings.UnprotectKey(encrypted) == "fake-secret-for-test", "DPAPI");
            using var custom = JsonDocument.Parse(VisionClient.CreatePayload(png, "custom-vision-model", "Resolver", ""));
            Check(!custom.RootElement.TryGetProperty("reasoning", out _), "Modelo personalizado sin parámetro incompatible");
            log += "PASS: cifrado DPAPI y modelo personalizado.\nTodas las pruebas locales pasaron; no se llamó a una API real.\n";
        }
        catch (Exception ex) { log += "FAIL: " + ex + "\n"; exit = 1; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), log);
        return exit;
    }
}
