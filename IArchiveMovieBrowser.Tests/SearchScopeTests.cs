using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class SearchScopeTests
{
    [Theory]
    [InlineData(SearchScope.Everything, "title:(buckaroo)")]
    [InlineData(SearchScope.WatchableVideo, "title:(buckaroo) AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "title:(buckaroo) AND NOT mediatype:movies")]
    public void BuildQuery_MapsEachScope(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery("buckaroo", scope));
    }

    [Fact]
    public void BuildQuery_KeepsUserTermsEscapeConvention()
    {
        // Same title:(...) shape plus the media restriction; whole expression is escaped
        // downstream just like the pre-scope query.
        Assert.Equal(
            "title:(buckaroo bonsai) AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery("buckaroo bonsai", SearchScope.WatchableVideo));
    }

    [Theory]
    [InlineData(SearchScope.Everything, "q=title%3A%28buckaroo%29")]
    [InlineData(SearchScope.WatchableVideo, "q=title%3A%28buckaroo%29%20AND%20mediatype%3Amovies")]
    [InlineData(
        SearchScope.RelatedMaterials,
        "q=title%3A%28buckaroo%29%20AND%20NOT%20mediatype%3Amovies")]
    public async Task Client_EncodesScopedQuery(SearchScope scope, string expectedQFragment)
    {
        const string json = "{\"response\":{\"numFound\":1,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        await client.SearchAsync(new InternetArchiveSearchRequest("buckaroo", 1, 25, scope));

        Uri uri = handler.Requests[0].RequestUri!;
        Assert.Contains(expectedQFragment, uri.Query);
    }

    [Fact]
    public void Request_DefaultsToEverythingScope()
    {
        var request = new InternetArchiveSearchRequest("buckaroo");
        Assert.Equal(SearchScope.Everything, request.Scope);
    }

    [Fact]
    public void ScopeLabels_AreExact()
    {
        Assert.Equal("Watchable video", SearchDisplayText.ScopeLabel(SearchScope.WatchableVideo));
        Assert.Equal("Related materials", SearchDisplayText.ScopeLabel(SearchScope.RelatedMaterials));
        Assert.Equal("Everything", SearchDisplayText.ScopeLabel(SearchScope.Everything));
    }

    [Fact]
    public void ScopeOptions_DefaultIsWatchableVideoFirst()
    {
        IReadOnlyList<SearchScope> options = SearchDisplayText.ScopeOptions();
        Assert.Equal(3, options.Count);
        Assert.Equal(SearchScope.WatchableVideo, options[0]);
        Assert.Equal(SearchScope.RelatedMaterials, options[1]);
        Assert.Equal(SearchScope.Everything, options[2]);
    }

    [Fact]
    public void RelatedMaterialsTooltip_IsExact()
    {
        Assert.Equal(
            "Non-video Internet Archive items matching your search, such as texts, audio, software, or images.",
            SearchDisplayText.RelatedMaterialsTooltip);
    }

    // --- Minimal fake handler (no live network) ------------------------------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = new List<HttpRequestMessage>();
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(System.Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status);
        response.Content = new StringContent(json);
        return response;
    }
}