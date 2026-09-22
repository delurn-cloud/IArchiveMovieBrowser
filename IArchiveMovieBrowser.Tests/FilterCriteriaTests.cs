using System;
using System.Net;
using System.Net.Http;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the optional Actor / creator and Year narrow-result filters:
/// criteria validation, query construction, and state propagation through paging/Refresh.
/// </summary>
public sealed class FilterCriteriaTests
{
    private const int CurrentYear = 1980; // accepted max is 1981

    // --- Criteria validation ---------------------------------------------------------

    [Fact]
    public void AllCriterionFieldsBlank_NoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.NoCriteria, r.State);
        Assert.Null(r.Criteria);
    }

    [Fact]
    public void TitleOnly_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria("War of the Worlds", null, null, SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.NotNull(r.Criteria);
        Assert.Equal("War of the Worlds", r.Criteria!.Title);
        Assert.Null(r.Criteria.Creator);
        Assert.Null(r.Criteria.Year);
        Assert.True(r.Criteria.HasAnyCriterion);
    }

    [Fact]
    public void ActorOnly_Valid_WhitespaceTrimmed()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, "  Gene Barry  ", null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal("Gene Barry", r.Criteria!.Creator);
        Assert.Null(r.Criteria.Title);
        Assert.Null(r.Criteria.Year);
    }

    [Fact]
    public void YearOnly_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1953", SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal(1953, r.Criteria!.Year);
        Assert.Null(r.Criteria.Title);
        Assert.Null(r.Criteria.Creator);
    }

    [Fact]
    public void TitleActorYear_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(
            "War of the Worlds", "Gene Barry", "1953", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.NotNull(r.Criteria);
        Assert.Equal("War of the Worlds", r.Criteria!.Title);
        Assert.Equal("Gene Barry", r.Criteria.Creator);
        Assert.Equal(1953, r.Criteria.Year);
    }

    [Fact]
    public void BlankActor_Ignored()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", "   ", null, SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Null(r.Criteria!.Creator);
    }

    [Fact]
    public void BlankYear_Ignored()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", null, "   ", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Null(r.Criteria!.Year);
    }

    [Theory]
    [InlineData("1800")]  // inclusive lower bound
    [InlineData("1981")]  // inclusive upper bound (currentYear + 1)
    [InlineData(" 1953 ")] // trimmed
    public void ValidYears_Accepted(string input)
    {
        var validation = FilterCriteriaLogic.ValidateYear(input, CurrentYear);
        Assert.Equal(YearValidationState.Valid, validation.State);
        Assert.NotNull(validation.Value);
    }

    [Theory]
    [InlineData("195")]      // three digits
    [InlineData("19533")]    // five digits
    [InlineData("19a3")]     // non-digit
    [InlineData("١٩٥٣")]     // non-ASCII (Arabic-Indic) digits
    [InlineData("19 53")]    // embedded whitespace
    [InlineData("1799")]     // below 1800
    [InlineData("1982")]     // above max
    public void InvalidYears_Rejected(string input)
    {
        var validation = FilterCriteriaLogic.ValidateYear(input, CurrentYear);
        Assert.Equal(YearValidationState.Invalid, validation.State);
        Assert.Null(validation.Value);
    }

    [Fact]
    public void InvalidYear_ReturnsClearMessageAndNoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "19x3", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYear, r.State);
        Assert.Null(r.Criteria);
        Assert.NotNull(r.Message);
        Assert.Contains("four-digit", r.Message!);
        Assert.Contains("1800", r.Message!);
        Assert.Contains("1981", r.Message!);
    }

    [Fact]
    public void InvalidYear_WithTitleAlsoReturnsInvalid()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", null, "99", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYear, r.State);
    }

    // --- Query construction ----------------------------------------------------------

    private static SearchCriteria Criteria(string? title, string? creator, string? year, SearchScope scope)
    {
        var r = FilterCriteriaLogic.BuildCriteria(title, creator, year, scope, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        return r.Criteria!;
    }

    [Fact]
    public void Query_SingleTokenCreator_IsUnquotedFieldToken()
    {
        // A remembered last name/part is a plain field token (no wildcards).
        Assert.Equal("creator:Barry",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", null, SearchScope.Everything)));
        Assert.Equal("creator:Barry AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", null, SearchScope.WatchableVideo)));
    }

    [Fact]
    public void Query_MultiTokenCreator_IsGroupedAnd()
    {
        Assert.Equal("creator:(Gene AND Barry)",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Gene Barry", null, SearchScope.Everything)));
        Assert.Equal("creator:(George AND Pal)",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "George Pal", null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_ActorAndYear_WithScope()
    {
        Assert.Equal("creator:Barry AND year:1953 AND NOT mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", "1953", SearchScope.RelatedMaterials)));
        Assert.Equal("creator:(Gene AND Barry) AND year:1953 AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Gene Barry", "1953", SearchScope.WatchableVideo)));
    }

    [Fact]
    public void Query_Combined_TitleCreatorYear_AllScopes()
    {
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953 AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", SearchScope.WatchableVideo)));
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953 AND NOT mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", SearchScope.RelatedMaterials)));
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", SearchScope.Everything)));
    }

    [Fact]
    public void Query_BooleanWordsAreLiteralCreatorText()
    {
        Assert.Equal("creator:\"OR\"",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "OR", null, SearchScope.Everything)));
        Assert.Equal("creator:\"AND\"",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "AND", null, SearchScope.Everything)));
        Assert.Equal("creator:\"NOT\"",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "NOT", null, SearchScope.Everything)));
        Assert.Equal("creator:\"or\"",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "or", null, SearchScope.Everything)));
        Assert.Equal("creator:(foo AND bar AND \"OR\")",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "foo bar OR", null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_UnsafePunctuationTokens_BecomeQuotedLiterals()
    {
        // Wildcard chars, colon, parens, brackets/braces, plus/minus, and ampersand/pipe are all
        // forced into safely escaped quoted literals so they can never act as Lucene syntax.
        Assert.Equal("creator:\"a*b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a*b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"a?b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a?b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"a:b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a:b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"(x)\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "(x)", null, SearchScope.Everything)));
        Assert.Equal("creator:\"[x]\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "[x]", null, SearchScope.Everything)));
        Assert.Equal("creator:\"{x}\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "{x}", null, SearchScope.Everything)));
        Assert.Equal("creator:\"+x\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "+x", null, SearchScope.Everything)));
        Assert.Equal("creator:\"-x\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "-x", null, SearchScope.Everything)));
        Assert.Equal("creator:\"a&b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a&b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"a|b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a|b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"&&\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "&&", null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_QuotesAndBackslashes_EscapedInsideQuotedLiteral()
    {
        // Inside the quoted literal, quotes and backslashes are escaped so they stay literal.
        Assert.Equal("creator:\"a\\\"b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a\"b", null, SearchScope.Everything)));
        Assert.Equal("creator:\"a\\\\b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a\\b", null, SearchScope.Everything)));
        Assert.Equal("creator:(\"Jones\\\\Smith\" AND \"\\\"Q\\\"\")", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Jones\\Smith \"Q\"", null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_NoUserInputCanAddExtraFieldOrOperatorClauses()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria(null, "Bob \"Scoops\" O'Brien & Bros (film) + Inc - LLC * \\ dir", null, SearchScope.Everything));
        // The whole text becomes one creator group; user content cannot escape into other fields
        // or produce standalone operators/groups/wildcards.
        Assert.StartsWith("creator:(", result);
        Assert.EndsWith(")", result);
        Assert.DoesNotContain("title:", result);
        Assert.DoesNotContain("year:", result);
        Assert.DoesNotContain("mediatype:", result);
    }

    private static int countOccurrences(string haystack, string needle)
    {
        int count = 0;
        int from = 0;
        while (true)
        {
            int at = haystack.IndexOf(needle, from);
            if (at < 0)
            {
                return count;
            }
            count++;
            from = at + needle.Length;
        }
    }

    // --- Summary text (actor-only / year-only) ----------------------------------------

    [Fact]
    public void Summary_ActorOnly_IsUnderstandable()
    {
        var criteria = Criteria(null, "Gene Barry", null, SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 12, 1);
        Assert.Contains("creator \"Gene Barry\"", summary);
        Assert.Contains("12 results", summary);
        Assert.Contains("Page 1", summary);
    }

    [Fact]
    public void Summary_YearOnly_IsUnderstandable()
    {
        var criteria = Criteria(null, null, "1953", SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 4, 2);
        Assert.Contains("year 1953", summary);
        Assert.Contains("Page 2", summary);
    }

    [Fact]
    public void Summary_TitleOnly_RetainsLegacyFormat()
    {
        var criteria = Criteria("buckaroo", null, null, SearchScope.Everything);
        Assert.Equal("Query: \"buckaroo\" · 128 results · Page 2",
            SearchDisplayText.QuerySummaryText(criteria, 128, 2));
    }

[Fact]
    public void UserFacingCreatorLabel_UsesMadeByCreditedTo_AndNoActorClaim()
    {
        Assert.Equal("Made by / credited to", SearchDisplayText.MadeByCreditedToLabel);
        Assert.DoesNotContain("actor", SearchDisplayText.MadeByCreditedToLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cast", SearchDisplayText.MadeByCreditedToLabel, StringComparison.OrdinalIgnoreCase);

        // The help text explains the underlying Internet Archive creator metadata source and
        // explicitly disclaims actor/cast searching.
        Assert.Contains("creator metadata", SearchDisplayText.MadeByCreditedToHelp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not search actors or cast", SearchDisplayText.MadeByCreditedToHelp, StringComparison.OrdinalIgnoreCase);
    }
    // --- State propagation (paging / Refresh reuse criteria) ----------------------------

    [Fact]
    public void Request_RetainsActorYearAndScope()
    {
        var criteria = Criteria("Title", "Creator", "1953", SearchScope.RelatedMaterials);
        var request = new InternetArchiveSearchRequest(criteria, 3, 25);
        Assert.Equal(criteria, request.Criteria);
        Assert.Equal(SearchScope.RelatedMaterials, request.Scope);
        Assert.Equal(3, request.Page);
    }

    [Fact]
    public void Request_RequiresAtLeastOneCriterion()
    {
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest(
            SearchCriteria.TitleOnly(null, SearchScope.Everything)));
    }

    [Fact]
    public async Task Client_PagingKeepsCriteriaTallScope()
    {
        const string json = "{\"response\":{\"numFound\":2,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var criteria = Criteria("War of the Worlds", "Gene Barry", "1953", SearchScope.WatchableVideo);
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 3, 25));

        Assert.Equal(2, handler.Requests.Count);
        string expression = Uri.EscapeDataString(
            "title:\"War of the Worlds\" AND creator:(Gene AND Barry) AND year:1953 AND mediatype:movies");
        Assert.Contains("q=" + expression + "&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("q=" + expression + "&", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("&page=1&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("&page=3&", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public void CompletedSearch_CarriesCriteria()
    {
        var criteria = Criteria("Title", "Creator", "1953", SearchScope.RelatedMaterials);
        var completed = new CompletedSearch(criteria, 7, 1, new System.Collections.Generic.List<InternetArchiveSearchResult>());
        Assert.Equal("Title", completed.Query);
        Assert.Equal(SearchScope.RelatedMaterials, completed.Scope);
        Assert.Equal("Creator", completed.Criteria.Creator);
        Assert.Equal(1953, completed.Criteria.Year);
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
