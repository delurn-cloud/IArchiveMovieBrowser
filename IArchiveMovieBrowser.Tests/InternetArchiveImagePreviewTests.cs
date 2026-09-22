using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the selected-result Internet Archive image preview: image-URL construction,
/// exact panel-state text/accessibility, stale-generation coordination, and client byte fetching
/// (all without live network).
/// </summary>
public sealed class InternetArchiveImagePreviewTests
{
    // --- URL construction -----------------------------------------------------------

    [Fact]
    public void BuildImageUrl_ValidIdentifier_ProducesExpectedUrl()
    {
        Assert.Equal(
            "https://archive.org/services/img/war-of-the-worlds",
            InternetArchiveImagePreview.BuildImageUrl("war-of-the-worlds"));
    }

    [Fact]
    public void BuildImageUrl_EncodesUrISignificantCharactersAsPathSegment()
    {
        // Spaces, '?', '#' are percent-encoded as a single path segment.
        string url = InternetArchiveImagePreview.BuildImageUrl("a b?c#d")!;
        Assert.StartsWith("https://archive.org/services/img/", url);
        Assert.DoesNotContain(" ", url);
        Assert.Contains("%20", url);
        Assert.Contains("%3F", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildImageUrl_BlankIdentifier_IsNull_NoPreview(string? identifier)
    {
        Assert.Null(InternetArchiveImagePreview.BuildImageUrl(identifier));
    }

    [Fact]
    public void BuildImageUrl_DoesNotUseTitleText()
    {
        string url = InternetArchiveImagePreview.BuildImageUrl("war-of-the-worlds")!;
        Assert.DoesNotContain("War of the Worlds", url);
        Assert.Contains("war-of-the-worlds", url);
    }

    // --- Exact panel-state text and accessibility ------------------------------------

    [Fact]
    public void PreviewTexts_AreExact()
    {
        // Details always represents one item, so there is no no-selection message.
        Assert.Equal("Loading Internet Archive image…", InternetArchiveImagePreview.LoadingMessage);
        Assert.Equal("No Internet Archive image available", InternetArchiveImagePreview.FallbackText);
        Assert.Equal(
            "No Internet Archive image is available for the selected item.",
            InternetArchiveImagePreview.FallbackAccessibleText);
    }

    [Fact]
    public void LoadedImageAccessibleText_UsesTitle_WithGenericFallback()
    {
        Assert.Equal(
            "Internet Archive image for The War of the Worlds",
            InternetArchiveImagePreview.LoadedImageAccessibleText("The War of the Worlds"));
        Assert.Equal(
            "Internet Archive image for the selected item",
            InternetArchiveImagePreview.LoadedImageAccessibleText(null));
        Assert.Equal(
            "Internet Archive image for the selected item",
            InternetArchiveImagePreview.LoadedImageAccessibleText("   "));
    }

// --- Client byte fetching via fake handler (no live network) ----------------------

    [Fact]
    public async Task GetImageBytes_ValidIdentifier_Success_ReturnsBytes()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4 };
        var handler = new ImageHandler(_ => expected, HttpStatusCode.OK, "image/png");
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        byte[]? result = await client.GetImageBytesAsync("war-of-the-worlds");

        Assert.NotNull(result);
        Assert.Equal(expected, result);
        Assert.Single(handler.Requests);
        Assert.StartsWith("https://archive.org/services/img/war-of-the-worlds", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetImageBytes_NonSuccessStatus_ReturnsNull()
    {
        var handler = new ImageHandler(_ => new byte[] { 1 }, HttpStatusCode.NotFound, "image/png");
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Assert.Null(await client.GetImageBytesAsync("war-of-the-worlds"));
    }

    [Fact]
    public async Task GetImageBytes_BlankIdentifier_ReturnsNull_NoHttp()
    {
        var handler = new ImageHandler(_ => new byte[] { 1 }, HttpStatusCode.OK, "image/png");
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Assert.Null(await client.GetImageBytesAsync("   "));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetImageBytes_Cancelled_ReturnsNull_NoException()
    {
        var handler = new ImageHandler(_ => { throw new OperationCanceledException(); }, HttpStatusCode.OK, "image/png");
        var client = new InternetArchiveApiClient(new HttpClient(handler));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        byte[]? result = await client.GetImageBytesAsync("war-of-the-worlds", cts.Token);
        Assert.Null(result);
    }

    // --- Minimal fake handler (no live network) --------------------------------------

    private sealed class ImageHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, byte[]?> _responder;
        private readonly HttpStatusCode _status;
        private readonly string _contentType;
        private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();

        public ImageHandler(System.Func<HttpRequestMessage, byte[]?> responder, HttpStatusCode status, string contentType)
        {
            _responder = responder;
            _status = status;
            _contentType = contentType;
        }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            byte[]? body = _responder(request);
            if (body is null)
            {
                throw new OperationCanceledException();
            }
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = content, StatusCode = _status });
        }
    }
}
