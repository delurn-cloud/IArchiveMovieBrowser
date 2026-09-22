using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the selected-video download: filename/naming/display helpers and the
/// streaming download client (fake handler + temporary filesystem, no live network).
/// </summary>
public sealed class DownloadsTests
{
    // --- Filename / naming helpers ------------------------------------------------------

    [Fact]
    public void SanitizeFileName_KeepsOrdinaryMp4Unchanged()
    {
        Assert.Equal("War of the Worlds 1953.mp4", Downloads.SanitizeFileName("War of the Worlds 1953.mp4"));
    }

    [Fact]
    public void SanitizeFileName_RemovesWindowsIllegalCharacters()
    {
        string? cleaned = Downloads.SanitizeFileName("a<b>:c\"d/e\\f|g?h*i.mp4");
        Assert.NotNull(cleaned);
        Assert.DoesNotContain("<", cleaned!);
        Assert.DoesNotContain(">", cleaned!);
        Assert.DoesNotContain("/", cleaned!);
        Assert.DoesNotContain("*", cleaned!);
        Assert.EndsWith(".mp4", cleaned!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("..")]
    public void SuggestedFileName_EmptyOrTraversal_ReturnsSafeNonNullDefault(string? name)
    {
        // Unusable raw names never become a destination path; a safe default filename is suggested.
        string suggested = Downloads.SuggestedFileName(name);
        Assert.False(string.IsNullOrWhiteSpace(suggested));
        Assert.EndsWith(".mp4", suggested);
    }

    [Fact]
    public void IsUnsafeRawFileName_RejectsTraversalAndSeparators()
    {
        Assert.True(Downloads.IsUnsafeRawFileName("a/b.mp4"));
        Assert.True(Downloads.IsUnsafeRawFileName("..secret"));
        Assert.False(Downloads.IsUnsafeRawFileName("film.mp4"));
    }

    [Fact]
    public void SuggestedFileName_DerivesFromSelectedFile()
    {
        Assert.Equal("War of the Worlds 1953.mp4", Downloads.SuggestedFileName("War of the Worlds 1953.mp4"));
    }

    [Fact]
    public void TempAndBackupPaths_AreSiblingWithSuffixes()
    {
        string finalPath = System.IO.Path.GetTempPath();
        Assert.EndsWith(".partial", Downloads.TempSiblingPath(finalPath));
        Assert.EndsWith(".iamb.bak", Downloads.BackupSiblingPath(finalPath));
    }

    // --- Progress / byte formatting -----------------------------------------------------

    [Fact]
    public void ProgressText_UnknownTotal_HasNoPercent()
    {
        string text = Downloads.ProgressText(new DownloadProgress(516L * 1024L * 1024L, null));
        Assert.StartsWith("Downloading… ", text);
        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void ProgressText_KnownTotal_IncludesPercent()
    {
        string text = Downloads.ProgressText(new DownloadProgress(516L * 1024L * 1024L, 1024L * 1024L * 1024L));
        Assert.Contains("%", text);
        Assert.Contains("of", text);
}
// --- Download client (fake handler + temporary filesystem) -----------------------------

    private string MakeTempDir()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iamb_dl_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static Uri canonical(string file = "video.mp4") =>
        InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", file);

    [Fact]
    public async Task Download_2xxTrusted_WriteBytesToFinal_NoPartialRemains()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var body = new byte[300000];
            for (int i = 0; i < body.Length; i++) { body[i] = (byte)(i % 251); }
            var handler = new DlHandler(HttpStatusCode.OK, body, "video/mp4");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), new RecordingListener(), CancellationToken.None);

            Assert.Equal(VideoDownloadOutcome.Completed, result.Outcome);
            Assert.True(System.IO.File.Exists(final));
            Assert.Equal((long)body.Length, new System.IO.FileInfo(final).Length);
            Assert.False(System.IO.File.Exists(final + ".partial"));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_2xxWithoutLength_Completes()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var handler = new DlHandler(HttpStatusCode.OK, new byte[] { 1, 2, 3, 4, 5 }, "video/mp4");
            handler.ExposeLength = false;
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None);

            Assert.Equal(VideoDownloadOutcome.Completed, result.Outcome);
            Assert.True(System.IO.File.Exists(final));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_2xxHtml_NonMedia_NoFinal()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var handler = new DlHandler(HttpStatusCode.OK, new byte[] { 1 }, "text/html");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None);

            Assert.Equal(VideoDownloadOutcome.NonMedia, result.Outcome);
            Assert.False(System.IO.File.Exists(final));
            Assert.False(System.IO.File.Exists(final + ".partial"));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Download_401Or403_Restricted(HttpStatusCode status)
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var handler = new DlHandler(status, new byte[] { 1 }, "text/plain");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            Assert.Equal(VideoDownloadOutcome.Restricted, (await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None)).Outcome);
            Assert.False(System.IO.File.Exists(final));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_404_Missing()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var handler = new DlHandler(HttpStatusCode.NotFound, new byte[] { 1 }, "text/plain");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            Assert.Equal(VideoDownloadOutcome.Missing, (await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None)).Outcome);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }
[Fact]
    public async Task Download_ExistingWithoutReplace_NoRequestNorTransfer()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            System.IO.File.WriteAllBytes(final, new byte[] { 9, 9, 9 });
            var handler = new DlHandler(HttpStatusCode.OK, new byte[] { 1 }, "video/mp4");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None);

            Assert.Equal(VideoDownloadOutcome.DestinationExists, result.Outcome);
            Assert.Equal(new byte[] { 9, 9, 9 }, System.IO.File.ReadAllBytes(final)); // untouched
            Assert.Equal(0, handler.RequestCount); // preflight: no HTTP request
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_ConfirmedReplace_ReplacesAndCleans()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            System.IO.File.WriteAllBytes(final, new byte[] { 9, 9, 9, 9, 9 });
            var body = new byte[] { 1, 2, 3 };
            var handler = new DlHandler(HttpStatusCode.OK, body, "video/mp4");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, true), null, CancellationToken.None);

            Assert.Equal(VideoDownloadOutcome.Completed, result.Outcome);
            Assert.Equal(body, System.IO.File.ReadAllBytes(final));
            Assert.False(System.IO.File.Exists(final + ".partial"));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_Cancelled_RemovesPartial_NoFinal()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var handler = new DlHandler(HttpStatusCode.OK, new byte[] { 1, 2, 3 }, "video/mp4");
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            VideoDownloadResult result = await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, cts.Token);

            Assert.Equal(VideoDownloadOutcome.Cancelled, result.Outcome);
            Assert.False(System.IO.File.Exists(final));
            Assert.False(System.IO.File.Exists(final + ".partial"));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Download_UnsafeFinalUri_NoWrite()
    {
        string dir = MakeTempDir();
        string final = System.IO.Path.Combine(dir, "out.mp4");
        try
        {
            var handler = new DlHandler(HttpStatusCode.OK, new byte[] { 1 }, "video/mp4");
            handler.FinalUriOverride = new Uri("https://evil.example/out.mp4", UriKind.Absolute);
            var client = new InternetArchiveApiClient(new HttpClient(handler));

            Assert.Equal(VideoDownloadOutcome.UnsafeFinalUri, (await client.DownloadFileAsync(
                new VideoDownloadRequest(canonical(), final, false), null, CancellationToken.None)).Outcome);
            Assert.False(System.IO.File.Exists(final));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>Fake handler that serves a fixed body and records request count.</summary>
    private sealed class DlHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;
        private readonly string _contentType;
        private int _requestCount;

        public bool ExposeLength { get; set; } = true;
        public Uri? FinalUriOverride { get; set; }

        public DlHandler(HttpStatusCode status, byte[] body, string contentType)
        {
            _status = status;
            _body = body;
            _contentType = contentType;
        }

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestCount++;
            var content = new ByteArrayContent(_body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            if (ExposeLength)
            {
                content.Headers.ContentLength = _body.Length;
            }
            HttpResponseMessage response = new HttpResponseMessage(_status) { Content = content, StatusCode = _status };
            HttpRequestMessage final = new HttpRequestMessage();
            final.RequestUri = FinalUriOverride is null ? request.RequestUri : FinalUriOverride;
            response.RequestMessage = final;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingListener : DownloadProgressListener
    {
        public int Count;
        public void OnProgress(DownloadProgress progress) => Count++;
    }
// --- Overwrite decision helper --------------------------------------------------------

    [Fact]
    public void ShouldReplace_OnlyExplicitAuthorizeIsTrue()
    {
        Assert.True(Downloads.ShouldReplace(Downloads.OverwriteDecision.Authorize));
        Assert.False(Downloads.ShouldReplace(Downloads.OverwriteDecision.Deny));
        Assert.False(Downloads.ShouldReplace(null));
    }
}