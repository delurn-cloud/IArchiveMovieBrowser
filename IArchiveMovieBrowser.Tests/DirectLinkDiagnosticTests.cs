using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the selected-video-link diagnostic: direct-URL path-segment encoding,
/// HTTP probing (headers only, body never downloaded), and user-visible outcome/summary text.
/// No live network.
/// </summary>
public sealed class DirectLinkDiagnosticTests
{
    // --- URI path-segment construction --------------------------------------------------

    [Fact]
    public void BuildDownloadUri_OrdinaryMp4_KeepsSlashBoundary()
    {
        Assert.Equal(
            "https://archive.org/download/item/video.mp4",
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4").ToString());
    }

    [Theory]
    [InlineData("item one", "my clip.mp4")]
    [InlineData("a#b", "film#1.mp4")]
    [InlineData("a%b", "100%done.mp4")]
    [InlineData("q?=x", "part?.mp4")]
    [InlineData("東京", "映画 (特集).mp4")]
    [InlineData("it'em", "O'Brien's (cut).mp4")]
    [InlineData("item", "video.mp4")]
    public void BuildDownloadUri_PerSegmentEncode_NoDoubleEncoding_KeepsSlashBoundary(
        string identifier,
        string filename)
    {
        Uri uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);

        // Canonical contract: each data segment is encoded independently (via EscapeDataString)
        // and joined by the literal slash boundary. Equality proves the whole URL is never
        // re-encoded as one opaque string and that existing %xx sequences are not double-encoded.
        string expected = "/download/"
            + Uri.EscapeDataString(identifier) + "/" + Uri.EscapeDataString(filename);
        Assert.Equal(expected, uri.AbsolutePath);
        Assert.StartsWith("/download/", uri.AbsolutePath);

        // The slash boundary is the only path separator introduced by the builder: the two
        // data segments never leak a '/' (single-segment validation forbids it) and are not
        // joined with any other separator.
        Assert.DoesNotContain("//", uri.AbsolutePath.Replace("download//", ""));

        // Reserve characters (#, ?) are percent-encoded and never left raw in either segment.
        if (identifier.Contains("#") || filename.Contains("#"))
        {
            Assert.DoesNotContain("#", uri.AbsolutePath);
        }
        if (identifier.Contains("?") || filename.Contains("?"))
        {
            Assert.DoesNotContain("?", uri.AbsolutePath);
        }
    }

[Fact]
    public void BuildDownloadUri_AbsoluteUri_EncodesSpacesAndPercentData()
    {
        // Transport representation (AbsoluteUri) reflects how the URL is sent: ordinary spaces
        // are percent-encoded as %20, never left as display characters.
        Uri spaces = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(
            "War of the Worlds 1953", "War of the Worlds 1953.mp4");
        Assert.Contains(
            "/War%20of%20the%20Worlds%201953.mp4",
            spaces.AbsoluteUri);

        // A raw literal "%20" inside a metadata filename is percent data, so the '%' is encoded
        // once: "%20" -> "%2520" (not pre-decoded, not double-encoded).
        Uri rawPercent = InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "name%20file.mp4");
        Assert.Contains("name%2520file.mp4", rawPercent.AbsoluteUri);
        Assert.DoesNotContain("name%20file.mp4", rawPercent.AbsoluteUri);

        // Fragment/query reserved characters in a path segment are encoded once.
        Uri reserved = InternetArchiveDownloadUrlBuilder.BuildDownloadUri("a#b", "p%q?r.mp4");
        Assert.DoesNotContain("#", reserved.AbsoluteUri);
        Assert.DoesNotContain("?", reserved.AbsoluteUri);
        Assert.Contains("%25", reserved.AbsoluteUri);
    }
    // --- Probe: success + video content type ----------------------------------------------

    [Fact]
    public async Task Probe_200VideoMp4_WithLength_SuccessTextIncludesSize()
    {
        var proof = new byte[] { 1, 2, 3, 4 };
        var handler = new LinkHandler(req => Response(HttpStatusCode.OK, "video/mp4", proof));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Uri uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4");
        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(uri);

        Assert.True(probe.GotResponse);
        Assert.False(probe.NetworkFailed);
        Assert.True(probe.IsSuccessStatus);
        string text = DirectLinkDiagnosticText.OutcomeText(probe);
        Assert.StartsWith("Video link is available — HTTP 200", text);
        Assert.Contains("video/mp4", text);
        Assert.Contains("4 B", text);
    }

    [Fact]
    public async Task Probe_200VideoMp4_NoLength_SuccessOmitsSize()
    {
        var handler = new LinkHandler(req => ResponseNoLength(HttpStatusCode.OK, "video/mp4"));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"));

        Assert.True(probe.IsSuccessStatus);
        Assert.Null(probe.ContentLength);
        string text = DirectLinkDiagnosticText.OutcomeText(probe);
        Assert.StartsWith("Video link is available — HTTP 200", text);
        Assert.DoesNotContain("B", text); // must not pretend a missing length is zero
    }
// --- Probe: non-video / restricted / not-found / other --------------------------------

    [Fact]
    public async Task Probe_200Html_NotIdentifiedAsVideo()
    {
        var handler = new LinkHandler(req => Response(HttpStatusCode.OK, "text/html", new byte[] { 1 }));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"));

        Assert.Equal(DirectLinkDiagnosticText.NotIdentifiedAsVideoText,
            DirectLinkDiagnosticText.OutcomeText(probe));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Probe_401Or403_Restricted(HttpStatusCode status)
    {
        var handler = new LinkHandler(req => Response(status, "text/plain", new byte[] { 1 }));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"));

        Assert.Equal(DirectLinkDiagnosticText.RestrictedText, DirectLinkDiagnosticText.OutcomeText(probe));
    }

    [Fact]
    public async Task Probe_404_NotFound()
    {
        var handler = new LinkHandler(req => Response(HttpStatusCode.NotFound, "text/plain", new byte[] { 1 }));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "missing.mp4"));

        Assert.Equal(DirectLinkDiagnosticText.NotFoundText, DirectLinkDiagnosticText.OutcomeText(probe));
    }

    [Fact]
    public async Task Probe_OtherNonSuccess_IncludesHttpStatus()
    {
        var handler = new LinkHandler(req => Response(HttpStatusCode.InternalServerError, "text/plain", new byte[] { 1 }));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"));

        Assert.Equal(
            "Internet Archive returned HTTP 500 for this selected file.",
            DirectLinkDiagnosticText.OutcomeText(probe));
    }

    // --- Probe: failure / cancellation ----------------------------------------------------

    [Fact]
    public async Task Probe_RequestException_NetworkFailure()
    {
        var handler = new LinkHandler(req => { throw new System.IO.IOException("boom"); });
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"));

        Assert.True(probe.NetworkFailed);
        Assert.Equal(DirectLinkDiagnosticText.NetworkFailureText, DirectLinkDiagnosticText.OutcomeText(probe));
    }

    [Fact]
    public async Task Probe_Cancelled_IsCancelled_NotNetworkFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // token requests cancellation before the request resolves
        var handler = new LinkHandler(req => { throw new OperationCanceledException(); });
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        DirectLinkProbe probe = await client.ProbeVideoLinkAsync(
            InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4"), cts.Token);

        Assert.True(probe.IsCancelled, "A caller-token cancellation must be classified as Cancelled.");
        Assert.False(probe.NetworkFailed, "Cancellation must not be misreported as a network failure.");
        Assert.NotEqual(DirectLinkDiagnosticText.NetworkFailureText, DirectLinkDiagnosticText.OutcomeText(probe));
        Assert.Equal(DirectLinkDiagnosticText.CancelledText, DirectLinkDiagnosticText.OutcomeText(probe));
    }

    // --- Display helpers -------------------------------------------------------------------

    [Fact]
    public void IsVideoLikeContentType_RecognizesVideo()
    {
        Assert.True(DirectLinkDiagnosticText.IsVideoLikeContentType("video/mp4"));
        Assert.True(DirectLinkDiagnosticText.IsVideoLikeContentType("video/quicktime"));
        Assert.False(DirectLinkDiagnosticText.IsVideoLikeContentType("text/html"));
        Assert.False(DirectLinkDiagnosticText.IsVideoLikeContentType(null));
        Assert.False(DirectLinkDiagnosticText.IsVideoLikeContentType(""));
    }

    [Fact]
    public void SummaryText_OmitsAbsentFields()
    {
        Uri uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "video.mp4");
        DirectLinkProbe probe = DirectLinkProbe.FromResponse(
            uri, uri, HttpStatusCode.OK, null, "video/mp4", null, null);
        string summary = DirectLinkDiagnosticText.SummaryText(probe);
        Assert.Contains("Requested URL: https://archive.org/download/item/video.mp4", summary);
        Assert.Contains("HTTP status: 200", summary);
        Assert.Contains("Content type: video/mp4", summary);
        Assert.DoesNotContain("Content length", summary);
        Assert.DoesNotContain("Content-Disposition", summary);
    }

    // --- Minimal fake handler (no live network) --------------------------------------------

    private sealed class LinkHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public LinkHandler(System.Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string contentType,
        byte[] body,
        string? disposition = null)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = body.Length;
        if (disposition is not null)
        {
            content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue(disposition);
        }
        return new HttpResponseMessage(status) { Content = content, StatusCode = status };
    }

    private static HttpResponseMessage ResponseNoLength(HttpStatusCode status, string contentType)
    {
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = null; // no Content-Length present
        return new HttpResponseMessage(status) { Content = content, StatusCode = status };
    }
}