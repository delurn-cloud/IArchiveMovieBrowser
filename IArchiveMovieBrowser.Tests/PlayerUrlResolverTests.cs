using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for resolve-then-launch: final-URI trust policy, launch-request bridging, and
/// client URL resolution (header-only; no live network, no actual player process).
/// </summary>
public sealed class PlayerUrlResolverTests
{
    private static Uri canonical(string filename = "video.mp4") =>
        InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", filename);

    // --- Final-URI trust policy --------------------------------------------------------

    [Fact]
    public void TrustPolicy_AcceptsHttpsArchiveOrgAndSubdomains()
    {
        Assert.True(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://archive.org/download/item/video.mp4", UriKind.Absolute)));
        Assert.True(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://ia901.us.archive.org/video.mp4", UriKind.Absolute)));
        Assert.True(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://dn1234.ca.archive.org/video.mp4", UriKind.Absolute)));
    }

    [Fact]
    public void TrustPolicy_RejectsNonHttpsAndForeignHosts()
    {
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("http://archive.org/download/item/video.mp4", UriKind.Absolute)));
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://evil.example/video.mp4", UriKind.Absolute)));
    }

    [Fact]
    public void TrustPolicy_RejectsHostSpoofing()
    {
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://evilarchive.org/video.mp4", UriKind.Absolute)));
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://archive.org.evil.example/video.mp4", UriKind.Absolute)));
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://archive.org@evil.example/video.mp4", UriKind.Absolute)));
    }

    [Fact]
    public void TrustPolicy_RejectsNonDefaultPortAndMissingUri()
    {
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(
            new Uri("https://archive.org:8080/download/item/video.mp4", UriKind.Absolute)));
        Assert.False(PlayerUrlResolver.IsTrustedFinalUri(null));
    }

    // --- Launch-request bridge (only resolved + trusted final reaches the launcher) ------

    [Fact]
    public void BuildLaunchRequest_ResolvedTrustedFinal_YieldsLaunchWithAbsoluteUri()
    {
        Uri finalUri = new Uri("https://ia901.us.archive.org/a%20b/file.mp4", UriKind.Absolute);
        var result = PlayerResolutionResult.Resolved(finalUri);

        ExternalPlayerLaunchRequest? launch =
            PlayerResolveThenLaunchLogic.BuildLaunchRequest(result, "C:\\vlc\\vlc.exe");

        Assert.NotNull(launch);
        Assert.Equal("C:\\vlc\\vlc.exe", launch!.ExecutablePath);
        Assert.Equal(finalUri.AbsoluteUri, launch.Url);
    }

    [Fact]
    public void BuildLaunchRequest_RejectsEveryNonResolvedOutcome()
    {
        string exe = "C:\\vlc\\vlc.exe";
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(PlayerResolutionResult.Cancelled(), exe));
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(PlayerResolutionResult.Restricted(), exe));
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(PlayerResolutionResult.Missing(), exe));
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(
            PlayerResolutionResult.NonSuccess(System.Net.HttpStatusCode.InternalServerError), exe));
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(
            PlayerResolutionResult.UnsafeFinal(null), exe));
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(
            PlayerResolutionResult.NetworkFailure(), exe));
    }

    [Fact]
    public void BuildLaunchRequest_RejectsResolvedButUntrustedFinal()
    {
        Uri evil = new Uri("https://evil.example/video.mp4", UriKind.Absolute);
        Assert.Null(PlayerResolveThenLaunchLogic.BuildLaunchRequest(
            PlayerResolutionResult.Resolved(evil), "C:\\vlc\\vlc.exe"));
    }
// --- Client URL resolution (fake handler, no live network) ---------------------------

    [Fact]
    public async Task Resolve_2xx_TrustedFinal_Resolved()
    {
        var handler = new ResolveHandler(HttpStatusCode.OK, _ => Body());
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        PlayerResolutionResult result = await client.ResolvePlayerUrlAsync(canonical());
        Assert.Equal(PlayerResolutionOutcome.Resolved, result.Outcome);
        Assert.NotNull(result.FinalUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Resolve_401Or403_Restricted(HttpStatusCode status)
    {
        var handler = new ResolveHandler(status, _ => Body());
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Assert.Equal(PlayerResolutionOutcome.Restricted,
            (await client.ResolvePlayerUrlAsync(canonical())).Outcome);
    }

    [Fact]
    public async Task Resolve_404_Missing()
    {
        var handler = new ResolveHandler(HttpStatusCode.NotFound, _ => Body());
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Assert.Equal(PlayerResolutionOutcome.Missing,
            (await client.ResolvePlayerUrlAsync(canonical())).Outcome);
    }

    [Fact]
    public async Task Resolve_OtherNonSuccess_NeverResolved()
    {
        var handler = new ResolveHandler(HttpStatusCode.InternalServerError, _ => Body());
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        PlayerResolutionResult result = await client.ResolvePlayerUrlAsync(canonical());
        Assert.Equal(PlayerResolutionOutcome.NonSuccessHttp, result.Outcome);
        Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task Resolve_Cancelled_IsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ResolveHandler(HttpStatusCode.OK, _ => { throw new OperationCanceledException(); });
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        PlayerResolutionResult result = await client.ResolvePlayerUrlAsync(canonical(), cts.Token);
        Assert.Equal(PlayerResolutionOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Resolve_RequestException_NetworkFailure()
    {
        var handler = new ResolveHandler(HttpStatusCode.OK, _ => { throw new System.IO.IOException("boom"); });
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        Assert.Equal(PlayerResolutionOutcome.NetworkFailure,
            (await client.ResolvePlayerUrlAsync(canonical())).Outcome);
    }

    // --- Minimal fake handler (no live network) ------------------------------------------

    private sealed class ResolveHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly System.Func<HttpRequestMessage, ByteArrayContent> _body;

        public ResolveHandler(HttpStatusCode status, System.Func<HttpRequestMessage, ByteArrayContent> body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ByteArrayContent content = _body(request);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            HttpResponseMessage response = new HttpResponseMessage(_status) { Content = content, StatusCode = _status };
            response.RequestMessage = request; // final URI = the request URI (trusted archive.org)
            return Task.FromResult(response);
        }
    }

    private static ByteArrayContent Body() => new ByteArrayContent(new byte[] { 1, 2, 3 });
}