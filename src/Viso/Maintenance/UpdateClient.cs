using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Viso;

internal sealed record UpdateRelease(Version Version, Uri Download, string Sha256, long Size);

internal sealed class UpdateClient : IDisposable
{
    internal static Version CurrentVersion => typeof(UpdateClient).Assembly.GetName().Version!;
    internal static string VersionLabel => CurrentVersion.ToString(3);
    private const long MaximumSize = 300 * 1024 * 1024;
    private readonly HttpClient client;

    internal UpdateClient(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Viso/" + VersionLabel);
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    internal async Task<UpdateRelease?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync("https://api.github.com/repos/srg-cp/viso/releases/latest", deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(deadline.Token);
        return ParseRelease(json, current);
    }

    internal static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag.Substring(1), out var version)) return null;
        // Assembly versions have a fourth component, release tags have three.
        if (new Version(version.Major, version.Minor, version.Build, 0) <= new Version(current.Major, current.Minor, Math.Max(0, current.Build), Math.Max(0, current.Revision))) return null;
        var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(item => item.GetProperty("name").GetString() == "Viso.exe");
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("El release aún no contiene Viso.exe. Inténtalo más tarde.");
        string expected = $"https://github.com/srg-cp/viso/releases/download/{tag}/Viso.exe";
        if (asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("El enlace de actualización no pertenece al release de Viso.");
        string digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("La actualización todavía no tiene una verificación válida.");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumSize) throw new InvalidDataException("El tamaño de la actualización no es válido.");
        return new UpdateRelease(version, new Uri(expected), digest.Substring(7).ToLowerInvariant(), size);
    }

    internal async Task<string> DownloadAsync(UpdateRelease release, string directory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = deadline.Token;
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "Viso.download");
        try
        {
            using var response = await client.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("La descarga tiene un tamaño inesperado.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > release.Size || received > MaximumSize) throw new InvalidDataException("La descarga supera el tamaño esperado.");
                    hash.AppendData(buffer, 0, count);
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((int)(received * 100 / release.Size));
                }
                if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("No se pudo verificar la actualización. Se conservará tu versión actual.");
            }
            return file;
        }
        catch
        {
            if (File.Exists(file)) File.Delete(file);
            throw;
        }
    }

    public void Dispose() => client.Dispose();
}
