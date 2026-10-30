using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Viso.Tests;

public sealed class UpdateClientTests
{
    private static string Release(string tag = "v2.5.0", string? url = null, string? digest = null, long size = 4, bool prerelease = false, bool draft = false)
        => JsonSerializer.Serialize(new { tag_name = tag, prerelease, draft, assets = new[] { new { name = "Viso.exe", size, digest = digest ?? "sha256:" + new string('a', 64), browser_download_url = url ?? $"https://github.com/srg-cp/viso/releases/download/{tag}/Viso.exe" } } });

    [Theory]
    [InlineData("v2.3.0")]
    [InlineData("v2.4.0")]
    [InlineData("v2.5.0-beta")]
    [InlineData("invalid")]
    public void CurrentOlderAndInvalidVersionsAreNotUpdates(string tag)
        => Assert.Null(UpdateClient.ParseRelease(Release(tag), new Version(2, 4, 0, 0)));

    [Fact]
    public void OnlyStablePublishedNewerReleaseIsSelected()
    {
        Assert.Null(UpdateClient.ParseRelease(Release(prerelease: true), new Version(2, 4, 0)));
        Assert.Null(UpdateClient.ParseRelease(Release(draft: true), new Version(2, 4, 0)));
        Assert.Equal(new Version(2, 5, 0), UpdateClient.ParseRelease(Release(), new Version(2, 4, 0))!.Version);
    }

    [Theory]
    [InlineData("https://evil.example/Viso.exe")]
    [InlineData("http://github.com/srg-cp/viso/releases/download/v2.5.0/Viso.exe")]
    [InlineData("https://github.com/other/viso/releases/download/v2.5.0/Viso.exe")]
    public void DownloadsOutsideExactReleaseAreRejected(string url)
        => Assert.Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Release(url: url), new Version(2, 4, 0)));

    [Theory]
    [InlineData("")]
    [InlineData("sha256:wrong")]
    public void MissingOrInvalidIntegrityIsRejected(string digest)
        => Assert.Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Release(digest: digest), new Version(2, 4, 0)));

    [Theory]
    [InlineData(0)]
    [InlineData(400000000)]
    public void InvalidSizeIsRejected(long size)
        => Assert.Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Release(size: size), new Version(2, 4, 0)));

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 4)]
    [InlineData(true, 5)]
    public async Task DownloadsAreKeptOnlyWhenSizeAndHashMatch(bool validHash, long size)
    {
        byte[] bytes = { 77, 90, 1, 2 };
        string hash = validHash ? Convert.ToHexString(SHA256.HashData(bytes)) : new string('a', 64);
        using var client = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        string job = Updater.CreateJob();
        try
        {
            var release = new UpdateRelease(new Version(2, 5, 0), new Uri("https://github.com/srg-cp/viso/releases/download/v2.5.0/Viso.exe"), hash, size);
            if (validHash && size == bytes.Length)
                Assert.Equal(bytes, File.ReadAllBytes(await client.DownloadAsync(release, job, null, CancellationToken.None)));
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, job, null, CancellationToken.None));
                Assert.Empty(Directory.GetFiles(job));
            }
        }
        finally { Updater.DeleteJob(job); }
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoExecutable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new UpdateClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 77, 90 }) }));
        string job = Updater.CreateJob();
        try
        {
            var release = new UpdateRelease(new Version(2, 5, 0), new Uri("https://github.com/srg-cp/viso/releases/download/v2.5.0/Viso.exe"), new string('a', 64), 2);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(release, job, null, cancellation.Token));
            Assert.Empty(Directory.GetFiles(job));
        }
        finally { Updater.DeleteJob(job); }
    }

    [Fact]
    public async Task PublicLookupUsesNoCredentialsAndHandlesNoReleases()
    {
        using var client = new UpdateClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/srg-cp/viso/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        Assert.Null(await client.CheckAsync(new Version(2, 4, 0), CancellationToken.None));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
