using System;
using System.Net;
using System.Net.Http;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the optional Made-by/credited-to (creator) and Year range narrow filters:
/// criteria validation, query construction, and state propagation through paging/Refresh.
/// </summary>
public sealed class FilterCriteriaTests
{
    private const int CurrentYear = 1980; // accepted max is 1981

    // --- Criteria validation ---------------------------------------------------------

    [Fact]
    public void AllCriterionFieldsBlank_NoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, null, null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.NoCriteria, r.State);
        Assert.Null(r.Criteria);
    }

    [Fact]
    public void TitleOnly_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria("War of the Worlds", null, null, null, SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.NotNull(r.Criteria);
        Assert.Equal("War of the Worlds", r.Criteria!.Title);
        Assert.Null(r.Criteria.Creator);
        Assert.Null(r.Criteria.YearFrom);
        Assert.Null(r.Criteria.YearTo);
        Assert.True(r.Criteria.HasAnyCriterion);
    }

    [Fact]
    public void ActorOnly_Valid_WhitespaceTrimmed()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, "  Gene Barry  ", null, null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal("Gene Barry", r.Criteria!.Creator);
        Assert.Null(r.Criteria.Title);
        Assert.Null(r.Criteria.YearFrom);
        Assert.Null(r.Criteria.YearTo);
    }

    [Fact]
    public void YearFromOnly_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1950", null, SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal(1950, r.Criteria!.YearFrom);
        Assert.Null(r.Criteria.YearTo);
    }

    [Fact]
    public void YearToOnly_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, null, "1955", SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Null(r.Criteria!.YearFrom);
        Assert.Equal(1955, r.Criteria.YearTo);
    }

    [Fact]
    public void SameYearEndpoints_Valid_IsExactYear()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1953", "1953", SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal(1953, r.Criteria!.YearFrom);
        Assert.Equal(1953, r.Criteria.YearTo);
        Assert.True(r.Criteria.IsExactYear);
    }

    [Fact]
    public void DifferingYearEndpoints_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1950", "1955", SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Equal(1950, r.Criteria!.YearFrom);
        Assert.Equal(1955, r.Criteria.YearTo);
        Assert.False(r.Criteria.IsExactYear);
    }

    [Fact]
    public void TitleActorYearRange_Valid()
    {
        var r = FilterCriteriaLogic.BuildCriteria(
            "War of the Worlds", "Gene Barry", "1953", "1953", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.NotNull(r.Criteria);
        Assert.Equal("War of the Worlds", r.Criteria!.Title);
        Assert.Equal("Gene Barry", r.Criteria.Creator);
        Assert.Equal(1953, r.Criteria.YearFrom);
        Assert.Equal(1953, r.Criteria.YearTo);
    }

    [Fact]
    public void BlankActor_Ignored()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", "   ", null, null, SearchScope.Everything, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Null(r.Criteria!.Creator);
    }

    [Fact]
    public void BlankYearEndpoints_Ignored()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", null, "   ", null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        Assert.Null(r.Criteria!.YearFrom);
        Assert.Null(r.Criteria.YearTo);
    }
[Theory]
    [InlineData("1800")]   // inclusive lower bound
    [InlineData("1981")]   // inclusive upper bound (currentYear + 1)
    [InlineData(" 1953 ")] // trimmed
    public void ValidateYear_AcceptsValid(string input)
    {
        Assert.Equal(YearValidationState.Valid, FilterCriteriaLogic.ValidateYear(input, CurrentYear).State);
    }

    [Theory]
    [InlineData("195")]      // three digits
    [InlineData("19533")]    // five digits
    [InlineData("19a3")]     // non-digit
    [InlineData("١٩٥٣")]     // non-ASCII (Arabic-Indic) digits
    [InlineData("19 53")]    // embedded whitespace
    [InlineData("1799")]     // below 1800
    [InlineData("1982")]     // above max
    public void ValidateYear_RejectsInvalid(string input)
    {
        Assert.Equal(YearValidationState.Invalid, FilterCriteriaLogic.ValidateYear(input, CurrentYear).State);
    }

    [Fact]
    public void InvalidYearFrom_ReturnsMessageAndNoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "19x3", null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYear, r.State);
        Assert.Null(r.Criteria);
        Assert.NotNull(r.Message);
        Assert.Contains("four-digit", r.Message!);
        Assert.Contains("1800", r.Message!);
        Assert.Contains("1981", r.Message!);
    }

    [Fact]
    public void InvalidYearTo_ReturnsMessageAndNoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, null, "19x3", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYear, r.State);
        Assert.Null(r.Criteria);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public void InvalidYearFrom_WithTitleAlsoReturnsInvalid()
    {
        var r = FilterCriteriaLogic.BuildCriteria("Title", null, "99", null, SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYear, r.State);
    }

    [Fact]
    public void YearFromLaterThanYearTo_InvalidYearRangeMessageAndNoCriteria()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1955", "1950", SearchScope.WatchableVideo, CurrentYear);
        Assert.Equal(CriteriaBuildState.InvalidYearRange, r.State);
        Assert.Null(r.Criteria);
        Assert.NotNull(r.Message);
        Assert.Equal("Year from must be earlier than or equal to Year to.", r.Message);
    }

    // --- Query construction ----------------------------------------------------------

    private static SearchCriteria Criteria(string? title, string? creator, string? yearFrom, string? yearTo, SearchScope scope)
    {
        var r = FilterCriteriaLogic.BuildCriteria(title, creator, yearFrom, yearTo, scope, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        return r.Criteria!;
    }
[Fact]
    public void Query_SingleTokenCreator_IsUnquotedFieldToken()
    {
        Assert.Equal("creator:Barry",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", null, null, SearchScope.Everything)));
        Assert.Equal("creator:Barry AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", null, null, SearchScope.WatchableVideo)));
    }

    [Fact]
    public void Query_MultiTokenCreator_IsGroupedAnd()
    {
        Assert.Equal("creator:(Gene AND Barry)",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Gene Barry", null, null, SearchScope.Everything)));
        Assert.Equal("creator:(George AND Pal)",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "George Pal", null, null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_CreatorAndExactYear_WithScope()
    {
        Assert.Equal("creator:Barry AND year:1953 AND NOT mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Barry", "1953", "1953", SearchScope.RelatedMaterials)));
        Assert.Equal("creator:(Gene AND Barry) AND year:1953 AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Gene Barry", "1953", "1953", SearchScope.WatchableVideo)));
    }

    [Fact]
    public void Query_ExactYearEndpoints_ProduceYearNotRange()
    {
        Assert.Equal("year:1953",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, "1953", "1953", SearchScope.Everything)));
    }

    [Fact]
    public void Query_TwoEndedRange_ProduceInclusiveRange()
    {
        Assert.Equal("year:[1950 TO 1955]",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, "1950", "1955", SearchScope.Everything)));
    }

    [Fact]
    public void Query_ToOnly_UsesMinAcceptedYear()
    {
        Assert.Equal("year:[1800 TO 1955]",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, null, "1955", SearchScope.Everything)));
    }

    [Theory]
    [InlineData(SearchScope.WatchableVideo, "year:1953 AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "year:1953 AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "year:1953")]
    public void Query_ExactYear_AllScopes(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, "1953", "1953", scope)));
    }

    [Theory]
    [InlineData(SearchScope.WatchableVideo, "year:[1950 TO 1955] AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, "year:[1950 TO 1955] AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "year:[1950 TO 1955]")]
    public void Query_Range_AllScopes(SearchScope scope, string expected)
    {
        Assert.Equal(expected, SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, "1950", "1955", scope)));
    }

    [Fact]
    public void Query_Combined_TitleCreatorExactYear_AllScopes()
    {
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953 AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", "1953", SearchScope.WatchableVideo)));
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953 AND NOT mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", "1953", SearchScope.RelatedMaterials)));
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Barry AND year:1953",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Barry", "1953", "1953", SearchScope.Everything)));
    }

    [Fact]
    public void Query_Combined_TitleCreatorRange_Watchable()
    {
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:Pal AND year:[1950 TO 1955] AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", "Pal", "1950", "1955", SearchScope.WatchableVideo)));
    }

    [Fact]
    public void Query_FromOnly_Everything()
    {
        Assert.Equal("year:[1950 TO 1981]",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, "1950", null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_ToOnly_Related()
    {
        Assert.Equal("year:[1800 TO 1955] AND NOT mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, null, null, "1955", SearchScope.RelatedMaterials)));
    }

    [Fact]
    public void Query_NoYearEndpoints_NoYearClause()
    {
        Assert.Equal("creator:Pal",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Pal", null, null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_TitleOnly_LegacyCompatibility()
    {
        IReadOnlyList<SearchScope> options = SearchDisplayText.ScopeOptions();
        foreach (SearchScope scope in options)
        {
            string legacy = SearchScopeQueryBuilder.BuildQuery("War of the Worlds", scope);
            string viaCriteria = SearchScopeQueryBuilder.BuildQuery(Criteria("War of the Worlds", null, null, null, scope));
            Assert.Equal(legacy, viaCriteria);
        }
    }
[Fact]
    public void Query_DeterministicClauseOrder_AndSingleScopeClause()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Title", "Creator", "1950", "1955", SearchScope.RelatedMaterials));
        int titlePos = result.IndexOf("title:");
        int creatorPos = result.IndexOf("creator:");
        int yearPos = result.IndexOf("year:");
        int scopePos = result.LastIndexOf("mediatype:");
        int scopeCount = countOccurrences(result, "mediatype:");
        Assert.True(titlePos < creatorPos);
        Assert.True(creatorPos < yearPos);
        Assert.True(yearPos < scopePos);
        Assert.Equal(1, scopeCount);
    }

    [Fact]
    public void Query_BooleanWordsAreLiteralCreatorText()
    {
        Assert.Equal("creator:\"OR\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "OR", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"AND\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "AND", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"NOT\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "NOT", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"or\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "or", null, null, SearchScope.Everything)));
        Assert.Equal("creator:(foo AND bar AND \"OR\")",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "foo bar OR", null, null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_UnsafePunctuationTokens_BecomeQuotedLiterals()
    {
        Assert.Equal("creator:\"a*b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a*b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"a?b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a?b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"a:b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a:b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"(x)\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "(x)", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"[x]\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "[x]", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"{x}\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "{x}", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"+x\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "+x", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"-x\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "-x", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"a&b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a&b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"a|b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a|b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"&&\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "&&", null, null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_QuotesAndBackslashes_EscapedInsideQuotedLiteral()
    {
        Assert.Equal("creator:\"a\\\"b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a\"b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:\"a\\\\b\"", SearchScopeQueryBuilder.BuildQuery(Criteria(null, "a\\b", null, null, SearchScope.Everything)));
        Assert.Equal("creator:(\"Jones\\\\Smith\" AND \"\\\"Q\\\"\")",
            SearchScopeQueryBuilder.BuildQuery(Criteria(null, "Jones\\Smith \"Q\"", null, null, SearchScope.Everything)));
    }

    [Fact]
    public void Query_NoUserInputCanAddExtraFieldOrOperatorClauses()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria(null, "Bob \"Scoops\" O'Brien & Bros (film) + Inc - LLC * \\ dir", null, null, SearchScope.Everything));
        Assert.StartsWith("creator:(", result);
        Assert.EndsWith(")", result);
        Assert.DoesNotContain("title:", result);
        Assert.DoesNotContain("year:", result);
        Assert.DoesNotContain("mediatype:", result);
    }
[Fact]
    public void Summary_ActorOnly_IsUnderstandable()
    {
        var criteria = Criteria(null, "Gene Barry", null, null, SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 12, 1);
        Assert.Contains("creator \"Gene Barry\"", summary);
        Assert.Contains("12 results", summary);
        Assert.Contains("Page 1", summary);
    }

    [Fact]
    public void Summary_ExactYear_IsUnderstandable()
    {
        var criteria = Criteria(null, null, "1953", "1953", SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 4, 2);
        Assert.Contains("year 1953", summary);
        Assert.Contains("Page 2", summary);
    }

    [Fact]
    public void Summary_TwoEndedRange_UsesYearsAndEnDash()
    {
        var criteria = Criteria(null, null, "1950", "1955", SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 40, 1);
        Assert.Contains("years 1950–1955", summary);
        Assert.Contains("40 results", summary);
    }

    [Fact]
    public void Summary_FromOnly_UsesFrom()
    {
        var criteria = Criteria(null, null, "1950", null, SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 20, 1);
        Assert.Contains("from 1950", summary);
        Assert.DoesNotContain("through", summary);
    }

    [Fact]
    public void Summary_ToOnly_UsesThrough()
    {
        var criteria = Criteria(null, null, null, "1955", SearchScope.WatchableVideo);
        string summary = SearchDisplayText.QuerySummaryText(criteria, 9, 3);
        Assert.Contains("through 1955", summary);
        Assert.Contains("Page 3", summary);
    }

    [Fact]
    public void Summary_TitleOnly_RetainsLegacyFormat()
    {
        var criteria = Criteria("buckaroo", null, null, null, SearchScope.Everything);
        Assert.Equal("Query: \"buckaroo\" · 128 results · Page 2",
            SearchDisplayText.QuerySummaryText(criteria, 128, 2));
    }

    [Fact]
    public void UserFacingCreatorLabel_UsesMadeByCreditedTo_AndNoActorClaim()
    {
        Assert.Equal("Made by / credited to", SearchDisplayText.MadeByCreditedToLabel);
        Assert.DoesNotContain("actor", SearchDisplayText.MadeByCreditedToLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cast", SearchDisplayText.MadeByCreditedToLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("creator metadata", SearchDisplayText.MadeByCreditedToHelp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not search actors or cast", SearchDisplayText.MadeByCreditedToHelp, StringComparison.OrdinalIgnoreCase);
    }

// --- State propagation (paging / Refresh reuse criteria) ----------------------------

    [Fact]
    public void Request_RetainsCreatorYearRangeAndScope()
    {
        var criteria = Criteria("Title", "Creator", "1950", "1955", SearchScope.RelatedMaterials);
        var request = new InternetArchiveSearchRequest(criteria, 3, 25);
        Assert.Equal(criteria, request.Criteria);
        Assert.Equal(SearchScope.RelatedMaterials, request.Scope);
        Assert.Equal(1950, request.Criteria.YearFrom);
        Assert.Equal(1955, request.Criteria.YearTo);
        Assert.Equal(3, request.Page);
    }

    [Fact]
    public void Request_RequiresAtLeastOneCriterion()
    {
        Assert.ThrowsAny<ArgumentException>(() => new InternetArchiveSearchRequest(
            SearchCriteria.TitleOnly(null, SearchScope.Everything)));
    }

    [Fact]
    public async Task Client_PagingKeepsCriteriaAndExactYearScope()
    {
        const string json = "{\"response\":{\"numFound\":2,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var criteria = Criteria("War of the Worlds", "Gene Barry", "1953", "1953", SearchScope.WatchableVideo);
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
    public async Task Client_PagingKeepsRangeExpression()
    {
        const string json = "{\"response\":{\"numFound\":2,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        var criteria = Criteria(null, "Pal", "1950", "1955", SearchScope.WatchableVideo);
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 2, 25));

        Assert.Single(handler.Requests);
        string expression = Uri.EscapeDataString("creator:Pal AND year:[1950 TO 1955] AND mediatype:movies");
        Assert.Contains("q=" + expression, handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public void CompletedSearch_CarriesCriteria()
    {
        var criteria = Criteria("Title", "Creator", "1950", "1955", SearchScope.RelatedMaterials);
        var completed = new CompletedSearch(criteria, 7, 1, new System.Collections.Generic.List<InternetArchiveSearchResult>());
        Assert.Equal("Title", completed.Query);
        Assert.Equal(SearchScope.RelatedMaterials, completed.Scope);
        Assert.Equal("Creator", completed.Criteria.Creator);
        Assert.Equal(1950, completed.Criteria.YearFrom);
        Assert.Equal(1955, completed.Criteria.YearTo);
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
