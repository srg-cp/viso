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
        public string ExpectedUrl = "https://api.openai.com/v1/responses";
        public bool GeminiAuthCorrect;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Payload = await request.Content!.ReadAsStringAsync(token);
            AuthCorrect = request.Headers.Authorization?.ToString() == "Bearer local-test-key";
            GeminiAuthCorrect = request.Headers.Contains("x-goog-api-key") && string.Join("", request.Headers.GetValues("x-goog-api-key")) == "local-gemini-key" && request.Headers.Authorization == null;
            Check(request.RequestUri!.AbsoluteUri == ExpectedUrl, "Endpoint oficial y clave ausente de URL");
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
            Check(root.GetProperty("reasoning").GetProperty("effort").GetString() == "low", "Razonamiento");
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
            log += "PASS: cifrado DPAPI y modelo personalizado.\n";
            handler.Status = HttpStatusCode.OK;
            handler.ExpectedUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent";
            handler.Body = "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"Oculto\"},{\"text\":\"Respuesta: B.\"}]},\"finishReason\":\"STOP\"}],\"usageMetadata\":{\"promptTokenCount\":80,\"candidatesTokenCount\":20,\"thoughtsTokenCount\":10}}";
            var gemini = await client.SolveAsync(png, "local-gemini-key", "gemini-3.5-flash-lite", TaskPrompts.Alternative, "", CancellationToken.None, "Gemini");
            Check(handler.GeminiAuthCorrect, "Gemini sin autenticación OpenAI");
            Check(gemini.Text == "Respuesta: B." && gemini.Usage.Contains("30"), "Texto visible y tokens de Gemini");
            using var geminiPayload = JsonDocument.Parse(handler.Payload);
            var geminiParts = geminiPayload.RootElement.GetProperty("contents")[0].GetProperty("parts");
            Check(geminiParts[1].GetProperty("inlineData").GetProperty("data").GetString() == Convert.ToBase64String(png), "PNG inline Gemini");
            Check(geminiParts[1].GetProperty("inlineData").GetProperty("mimeType").GetString() == "image/png", "MIME Gemini");
            Check(geminiParts[0].GetProperty("text").GetString()!.Contains("alternativa correcta"), "Modo alternativas");
            Check(geminiPayload.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString() == "high", "Razonamiento alto Gemini 3.5 Lite");
            Check(geminiPayload.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() == 16384, "Margen para razonamiento y salida");
            Check(geminiPayload.RootElement.GetProperty("generationConfig").GetProperty("mediaResolution").GetString() == "MEDIA_RESOLUTION_HIGH", "Lectura de imagen detallada");
            Check(geminiPayload.RootElement.GetProperty("generationConfig").GetProperty("temperature").GetDouble() == 1.0, "Temperatura recomendada Gemini 3");
            using var lite25 = JsonDocument.Parse(VisionClient.CreateGeminiPayload(png, TaskPrompts.Alternative, "gemini-2.5-flash-lite"));
            Check(lite25.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32() == 8192, "Gemini 2.5 usa budget");
            using var lite31 = JsonDocument.Parse(VisionClient.CreateGeminiPayload(png, TaskPrompts.Alternative, "gemini-3.1-flash-lite"));
            Check(lite31.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString() == "high", "Razonamiento alto Gemini 3.1 Lite");
            using var unknownGemini = JsonDocument.Parse(VisionClient.CreateGeminiPayload(png, TaskPrompts.Alternative, "custom-vision"));
            Check(!unknownGemini.RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _), "No imponer thinking a modelos desconocidos");
            using var completion = JsonDocument.Parse(VisionClient.CreateGeminiPayload(png, TaskPrompts.Complete));
            Check(completion.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!.Contains("únicamente las palabras"), "Modo completar Gemini");
            using var openAiCompletion = JsonDocument.Parse(VisionClient.CreatePayload(png, "gpt-5.4-mini", TaskPrompts.Complete, ""));
            Check(openAiCompletion.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!.Contains("únicamente las palabras"), "Modo completar OpenAI");
            log += "PASS: Gemini con autenticación independiente, PNG exacto y dos modos en ambos proveedores.\n";
            log += "PASS: razonamiento alto, resolución alta, temperatura recomendada y margen de 16384 tokens.\n";
            try { VisionClient.ParseGeminiAnswer("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Parcial\"}]},\"finishReason\":\"MAX_TOKENS\"}]}"); throw new Exception("Se mostró una solución incompleta."); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("incompleta"), "No mostrar soluciones incompletas"); }
            try { VisionClient.ParseGeminiAnswer("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"); throw new Exception("No se detectó bloqueo."); }
            catch (InvalidOperationException) { }
            try { VisionClient.ParseGeminiAnswer("{\"candidates\":[]}"); throw new Exception("No se detectó ausencia de candidatos."); }
            catch (InvalidOperationException) { }
            handler.Status = HttpStatusCode.BadRequest;
            handler.Body = "{\"error\":{\"message\":\"Invalid local-gemini-key\"}}";
            try
            {
                await client.SolveAsync(png, "local-gemini-key", "gemini-3.5-flash-lite", TaskPrompts.Alternative, "", CancellationToken.None, "Gemini");
                throw new Exception("No se detectó error Gemini.");
            }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("Gemini") && !ex.Message.Contains("local-gemini-key"), "Gemini clave oculta"); }
            var credentials = new Settings { EncryptedKey = Settings.ProtectKey("openai-fake-key"), EncryptedGeminiKey = Settings.ProtectKey("gemini-fake-key") };
            string serialized = JsonSerializer.Serialize(credentials);
            var restored = JsonSerializer.Deserialize<Settings>(serialized)!;
            Check(!serialized.Contains("openai-fake-key") && !serialized.Contains("gemini-fake-key") && restored.ReadKey() == "openai-fake-key" && restored.ReadGeminiKey() == "gemini-fake-key", "Claves independientes cifradas");
            string testImagePath = Path.Combine(Path.GetTempPath(), "CapturaTutor-image-test-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using var bitmap = new System.Drawing.Bitmap(80, 40);
                bitmap.SetPixel(10, 10, System.Drawing.Color.Red);
                bitmap.Save(testImagePath, System.Drawing.Imaging.ImageFormat.Png);
                var imported = ScreenCapture.LoadImage(testImagePath);
                Check(imported.Preview.PixelWidth == 80 && imported.Preview.PixelHeight == 40, "Importación sin reducir resolución");
                using var stream = new MemoryStream(imported.Png);
                using var converted = new System.Drawing.Bitmap(stream);
                Check(converted.GetPixel(10, 10).ToArgb() == System.Drawing.Color.Red.ToArgb(), "Importación conserva píxeles");
                Check(imported.Bounds.Width == 80 && imported.Bounds.Height == 40, "Dimensiones de imagen importada");
            }
            finally { if (File.Exists(testImagePath)) File.Delete(testImagePath); }
            log += "PASS: archivo de imagen importado conserva dimensiones y píxeles.\n";
            var screenBounds = new System.Drawing.Rectangle(-1920, 100, 1920, 1080);
            var selectedBounds = new System.Drawing.Rectangle(-1800, 250, 600, 400);
            var saved = CaptureArea.Create("test-monitor", screenBounds, selectedBounds);
            var persisted = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { Region = true, SavedArea = saved, HotkeyModifiers = 3, HotkeyVirtualKey = 0x51 }))!;
            Check(persisted.Region && persisted.SavedArea!.Resolve("test-monitor", screenBounds) == selectedBounds, "Área guardada se restaura");
            Check(persisted.SavedArea!.Resolve("test-monitor", new System.Drawing.Rectangle(0, 0, 1920, 1080)) == new System.Drawing.Rectangle(120, 150, 600, 400), "Área relativa al monitor");
            Check(persisted.HotkeyModifiers == 3 && persisted.HotkeyVirtualKey == 0x51, "Atajo guardado se restaura");
            try { saved.Resolve("other-monitor", screenBounds); throw new Exception("Se aceptó otro monitor."); } catch (ArgumentException) { }
            try { saved.Resolve("test-monitor", new System.Drawing.Rectangle(0, 0, 1280, 720)); throw new Exception("Se aceptó otra resolución."); } catch (ArgumentException) { }
            try { CaptureArea.Create("test-monitor", screenBounds, new System.Drawing.Rectangle(-1800, 250, int.MaxValue, 400)); throw new Exception("Se aceptó un área fuera del monitor."); } catch (ArgumentException) { }
            Check(GlobalHotkey.IsValid(3, 0x51) && !GlobalHotkey.IsValid(0, 0x51) && !GlobalHotkey.IsValid(16, 0x51), "Validación de combinaciones");
            Check(GlobalHotkey.Label(3, 0x51) == "Ctrl + Alt + Q", "Nombre legible del atajo");
            log += "PASS: persistencia de área/atajo, monitores con origen negativo y rechazo de cambios de monitor/resolución.\n";
            log += "PASS: Gemini parcial, bloqueo, sin texto, errores sin clave y dos credenciales cifradas.\nTodas las pruebas locales pasaron; no se llamó a una API real.\n";
        }
        catch (Exception ex) { log += "FAIL: " + ex + "\n"; exit = 1; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), log);
        return exit;
    }
}
