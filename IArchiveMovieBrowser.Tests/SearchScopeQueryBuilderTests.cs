using System;
using System.Net;
using System.Net.Http;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the literal-title-phrase query building: multi-word input must become a
/// single quoted title phrase in the IA query rather than a broad OR of individual terms.
/// </summary>
public sealed class SearchScopeQueryBuilderTests
{
    // --- Multi-word title phrase, all scopes --------------------------------------

    [Theory]
    [InlineData(SearchScope.WatchableVideo, "title:\"War of the Worlds\" AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "title:\"War of the Worlds\" AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "title:\"War of the Worlds\"")]
    public void BuildQuery_WarOfTheWorldsIsOnePhrasePerScope(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery("War of the Worlds", scope));
    }

    [Theory]
    [InlineData(SearchScope.WatchableVideo, "title:\"Buckaroo Banzai\" AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "title:\"Buckaroo Banzai\" AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "title:\"Buckaroo Banzai\"")]
    public void BuildQuery_BuckarooBanzaiIsOnePhrasePerScope(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery("Buckaroo Banzai", scope));
    }

    // --- Single-word input stays a title search -----------------------------------

    [Theory]
    [InlineData(SearchScope.WatchableVideo, "title:\"Metropolis\" AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "title:\"Metropolis\" AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "title:\"Metropolis\"")]
    public void BuildQuery_SingleWordIsTitlePhrasePerScope(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery("Metropolis", scope));
    }

    // --- Whitespace handling --------------------------------------------------------

    [Fact]
    public void BuildQuery_TrimsLeadingAndTrailingWhitespace()
    {
        Assert.Equal(
            "title:\"War of the Worlds\"",
            SearchScopeQueryBuilder.BuildQuery("   War of the Worlds   ", SearchScope.Everything));
        Assert.Equal(
            "title:\"War of the Worlds\"",
            SearchScopeQueryBuilder.BuildQuery("\tWar of the Worlds\n", SearchScope.Everything));
    }

    [Fact]
    public void BuildQuery_PreservesInternalOrdinarySpaces()
    {
        // An ordinary internal space between words is part of the literal phrase.
        Assert.Equal(
            "title:\"Perry Mason\"",
            SearchScopeQueryBuilder.BuildQuery("Perry Mason", SearchScope.Everything));
    }

    // --- Query-syntax characters are treated as literals ----------------------------

    [Fact]
    public void BuildQuery_TreatsQueryLikeCharactersAsLiterals_NotOperators()
    {
        // Quotes and backslashes are escaped so they cannot terminate/alter the phrase; the
        // remaining Lucene operators (colon, parens, +/-/-, wildcards, Boolean words) are inert.
        string input = "don't \"stop the (music)\" AND -all +of *this ?now";
        string result = SearchScopeQueryBuilder.BuildQuery(input, SearchScope.WatchableVideo);

        // The whole input stays inside one quoted phrase; embedded quotes are escaped.
        Assert.Equal(
            "title:\"don't \\\"stop the (music)\\\" AND -all +of *this ?now\" AND mediatype:movies",
            result);
    }

    [Fact]
    public void BuildQuery_KeepsColonParensPlusMinusWildcardsInsidePhrase()
    {
        string input = "a:b (c) +d -e f* g?";
        Assert.Equal(
            "title:\"a:b (c) +d -e f* g?\"",
            SearchScopeQueryBuilder.BuildQuery(input, SearchScope.Everything));
    }

    // --- URI encoding through the existing client -----------------------------------

    [Fact]
    public async Task Client_PercentEncodesBuiltPhraseExpression()
    {
        const string json = "{\"response\":{\"numFound\":1,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        await client.SearchAsync(new InternetArchiveSearchRequest("War of the Worlds", 1, 25,
            SearchScope.WatchableVideo));

        Uri uri = handler.Requests[0].RequestUri!;
        string expected = Uri.EscapeDataString("title:\"War of the Worlds\" AND mediatype:movies");
        Assert.Contains("q=" + expected, uri.Query);
        // The literal user text and quotes/spaces are percent-encoded, never concatenated raw.
        Assert.DoesNotContain("War of the Worlds", uri.Query);
    }

    // --- Paging and Refresh retain the phrase + scope --------------------------------

    [Fact]
    public async Task Client_PagingKeepsSamePhraseAndScope()
    {
        const string json = "{\"response\":{\"numFound\":2,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        // Page 1 and later page 3 (Refresh/Next/Previous all reuse the raw query + scope).
        await client.SearchAsync(new InternetArchiveSearchRequest("Buckaroo Banzai", 1, 25,
            SearchScope.WatchableVideo));
        await client.SearchAsync(new InternetArchiveSearchRequest("Buckaroo Banzai", 3, 25,
            SearchScope.WatchableVideo));

        Assert.Equal(2, handler.Requests.Count);
        string expression = Uri.EscapeDataString("title:\"Buckaroo Banzai\" AND mediatype:movies");
        Assert.Contains("q=" + expression + "&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("q=" + expression + "&", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("&page=1&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("&page=3&", handler.Requests[1].RequestUri!.Query);
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