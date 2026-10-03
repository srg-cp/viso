using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CapturaTutor;

internal static class ImageEvaluation
{
    // Explicit diagnostic command only. Normal use makes one request per click.
    public static async Task<int> RunAsync(string[] args)
    {
        var results = new List<object>();
        string model = "";
        string error = "";
        bool passed = true;
        try
        {
            string Get(string flag, string fallback = "")
            {
                int index = Array.IndexOf(args, flag);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            }
            var config = Settings.Load(out string warning);
            if (!string.IsNullOrEmpty(warning)) throw new InvalidOperationException(warning);
            model = config.GeminiModel;
            string key = config.ReadGeminiKey();
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("La prueba real requiere una clave Gemini guardada.");
            var capture = ScreenCapture.LoadImage(Get("--evaluate-image"));
            string crop = Get("--crop");
            if (!string.IsNullOrWhiteSpace(crop))
            {
                string[] numbers = crop.Split(',');
                if (numbers.Length != 4) throw new ArgumentException("Usa --crop x,y,ancho,alto.");
                var region = new Int32Rect(int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]), int.Parse(numbers[3]));
                var cropped = new CroppedBitmap(capture.Preview, region);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                byte[] bytes = stream.ToArray();
                capture = new Capture(bytes, ScreenCapture.ToPreview(bytes), new System.Drawing.Rectangle(0, 0, region.Width, region.Height));
            }
            int runs = Math.Clamp(int.Parse(Get("--runs", "1")), 1, 3);
            string expected = Get("--expected").Trim().ToLowerInvariant();
            if (expected.Length != 1) throw new ArgumentException("Indica la letra esperada con --expected.");
            using var client = new VisionClient();
            for (int i = 0; i < runs; i++)
            {
                var timer = Stopwatch.StartNew();
                // Expected answer is never included in the provider request.
                var answer = await client.SolveAsync(capture.Png, key, model, TaskPrompts.Alternative, "", CancellationToken.None, "Gemini");
                var match = Regex.Match(answer.Text.Trim(), @"^(?:Respuesta\s*:\s*)?[*\s]*([a-z])(?:[.)\s:\-]|$)", RegexOptions.IgnoreCase);
                bool correct = match.Success && match.Groups[1].Value.ToLowerInvariant() == expected;
                passed &= correct;
                results.Add(new { run = i + 1, answer = answer.Text, usage = answer.Usage, seconds = Math.Round(timer.Elapsed.TotalSeconds, 1), expectedMatch = correct });
                // Save after each request so interrupted evaluations preserve the evidence.
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "evaluation-result.json"), JsonSerializer.Serialize(new { model, expected, passed, results }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex)
        {
            passed = false;
            error = ex.Message;
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "evaluation-result.json"), JsonSerializer.Serialize(new { model, passed, error, results }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
