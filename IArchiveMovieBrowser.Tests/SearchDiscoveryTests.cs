using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Offline coverage for the discovery-search upgrade: phrase grammar, emitted query construction,
/// ordering parameters, validation, lifecycle propagation, summaries, and reset semantics.
///
/// Every assertion is on an emitted query string, a request parameter, or a display string. No test
/// asserts a live Internet Archive result count.
/// </summary>
public sealed class SearchDiscoveryTests
{
    private const int CurrentYear = 2026;

    // --- Helpers ----------------------------------------------------------------------

    private static SearchCriteria Criteria(
        string? title = null,
        string? creator = null,
        int? yearFrom = null,
        int? yearTo = null,
        SearchScope scope = SearchScope.WatchableVideo)
    {
        return SearchCriteria.Validated(title, creator, yearFrom, yearTo, CurrentYear, scope);
    }

    private static SearchCriteria WithKeywords(
        string raw,
        SearchKeywordFields fields = SearchKeywordFields.Subjects,
        SearchKeywordMatchMode mode = SearchKeywordMatchMode.AllTerms,
        SearchCriteria? seed = null)
    {
        SearchKeywordMatchMode effectiveMode = mode;
        IReadOnlyList<string> terms;

        if (mode == SearchKeywordMatchMode.ExactPhrase)
        {
            Assert.True(SearchKeywordPhraseParser.TryParseExactPhrase(raw, out string? phrase, out _));
            terms = string.IsNullOrEmpty(phrase) ? new List<string>() : new List<string> { phrase! };
        }
        else
        {
            Assert.True(SearchKeywordPhraseParser.TryParseTerms(raw, out IReadOnlyList<string>? parsed, out _));
            terms = parsed!;
        }

        return (seed ?? Criteria()).WithKeywords(terms, fields, effectiveMode);
    }

    private static SearchCriteria WithExclusions(
        string raw,
        SearchCriteria? seed = null)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseTerms(raw, out IReadOnlyList<string>? parsed, out _));
        return (seed ?? Criteria()).WithExcludedTerms(parsed!);
    }

    private static string Query(SearchCriteria criteria) => SearchScopeQueryBuilder.BuildQuery(criteria);

    // --- Phrase grammar: terms ---------------------------------------------------------

    [Theory]
    [InlineData("british mystery", new[] { "british", "mystery" })]
    [InlineData("british,mystery", new[] { "british", "mystery" })]
    [InlineData("  british ,, mystery  ", new[] { "british", "mystery" })]
    [InlineData("british mystery, western", new[] { "british", "mystery", "western" })]
    [InlineData("british", new[] { "british" })]
    public void Terms_SplitsOnWhitespaceAndCommas(string input, string[] expected)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseTerms(input, out IReadOnlyList<string>? terms, out _));
        Assert.Equal(expected, terms);
    }

    [Theory]
    [InlineData("\"country house\"", new[] { "country house" })]
    [InlineData("british \"murder mystery\" western", new[] { "british", "murder mystery", "western" })]
    [InlineData("\"a\",\"b\"", new[] { "a", "b" })]
    [InlineData("\"British mystery\"", new[] { "British mystery" })]
    public void Terms_QuotedPhraseIsExactlyOneTerm(string input, string[] expected)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseTerms(input, out IReadOnlyList<string>? terms, out _));
        Assert.Equal(expected, terms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(" , , ")]
    public void Terms_DelimiterOnlyInputIsEmptyNotInvalid(string? input)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseTerms(input, out IReadOnlyList<string>? terms, out SearchKeywordParseError error));
        Assert.Empty(terms!);
        Assert.Equal(SearchKeywordParseError.None, error);
    }

    [Theory]
    [InlineData("\"country house", SearchKeywordParseError.UnterminatedQuote)]
    [InlineData("country \"house", SearchKeywordParseError.UnterminatedQuote)]
    [InlineData("british \"murder", SearchKeywordParseError.UnterminatedQuote)]
    [InlineData("\"\"", SearchKeywordParseError.EmptyQuotedPhrase)]
    [InlineData("british \"\" western", SearchKeywordParseError.EmptyQuotedPhrase)]
    [InlineData("un\"quoted", SearchKeywordParseError.EmbeddedQuote)]
    [InlineData("mystery\"", SearchKeywordParseError.EmbeddedQuote)]
    [InlineData("\"a\"b", SearchKeywordParseError.TextAfterClosingQuote)]
    [InlineData("\"a \"b\" c\"", SearchKeywordParseError.TextAfterClosingQuote)]
    public void Terms_RejectsMalformedQuotesRatherThanReinterpreting(string input, SearchKeywordParseError expected)
    {
        Assert.False(SearchKeywordPhraseParser.TryParseTerms(input, out IReadOnlyList<string>? terms, out SearchKeywordParseError error));
        Assert.Null(terms);
        Assert.Equal(expected, error);
    }

    // --- Phrase grammar: exact phrase --------------------------------------------------

    [Theory]
    [InlineData("British mystery", "British mystery")]
    [InlineData("  country house  ", "country house")]
    [InlineData("\"country house\"", "country house")]
    [InlineData("  \"country house\"  ", "country house")]
    [InlineData("Agatha Christie", "Agatha Christie")]
    [InlineData("a, b", "a, b")] // commas are literal in exact-phrase mode
    public void ExactPhrase_TreatsWholeEntryAsOneTerm(string input, string expected)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseExactPhrase(input, out string? phrase, out _));
        Assert.Equal(expected, phrase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , , ")]
    public void ExactPhrase_WhitespaceOrDelimiterOnlyIsEmpty(string? input)
    {
        Assert.True(SearchKeywordPhraseParser.TryParseExactPhrase(input, out string? phrase, out SearchKeywordParseError error));
        Assert.Null(phrase);
        Assert.Equal(SearchKeywordParseError.None, error);
    }

    [Theory]
    [InlineData("\"\"", SearchKeywordParseError.EmptyQuotedPhrase)]
    [InlineData("\"   \"", SearchKeywordParseError.EmptyQuotedPhrase)]
    [InlineData("\"country house", SearchKeywordParseError.UnexpectedQuoteInPhrase)]
    [InlineData("country \"house", SearchKeywordParseError.UnexpectedQuoteInPhrase)]
    [InlineData("\"a \"b\" c\"", SearchKeywordParseError.UnexpectedQuoteInPhrase)]
    public void ExactPhrase_RejectsRemainingQuotes(string input, SearchKeywordParseError expected)
    {
        Assert.False(SearchKeywordPhraseParser.TryParseExactPhrase(input, out string? phrase, out SearchKeywordParseError error));
        Assert.Null(phrase);
        Assert.Equal(expected, error);
    }

    // --- Keyword query construction ---------------------------------------------------

    [Fact]
    public void Keywords_AllTerms_EmitsParenthesizedAndGroup()
    {
        Assert.Equal("(subject:\"British\" AND subject:\"mystery\") AND mediatype:movies",
            Query(WithKeywords("British mystery")));
    }

    [Fact]
    public void Keywords_AnyTerms_EmitsParenthesizedOrGroup()
    {
        Assert.Equal("(subject:\"British\" OR subject:\"mystery\") AND mediatype:movies",
            Query(WithKeywords("British mystery", mode: SearchKeywordMatchMode.AnyTerms)));
    }

    [Fact]
    public void Keywords_SingleTerm_EmitsNoRedundantParentheses()
    {
        Assert.Equal("subject:\"british\" AND mediatype:movies",
            Query(WithKeywords("british")));
    }

    [Fact]
    public void Keywords_QuotedPhrase_AndExactPhrase_ProduceTheSameClause()
    {
        string quoted = Query(WithKeywords("\"British mystery\""));
        string exact = Query(WithKeywords("British mystery", mode: SearchKeywordMatchMode.ExactPhrase));

        Assert.Equal("subject:\"British mystery\" AND mediatype:movies", quoted);
        Assert.Equal(quoted, exact);
    }

    [Fact]
    public void Keywords_SubjectsAndDescriptions_OrsAcrossFieldsPerTerm()
    {
        Assert.Equal("((subject:\"British\" OR description:\"British\") AND (subject:\"mystery\" OR description:\"mystery\")) AND mediatype:movies",
            Query(WithKeywords("British mystery", fields: SearchKeywordFields.SubjectsAndDescriptions)));
    }

    [Fact]
    public void Keywords_SubjectsAndDescriptions_SingleTerm_KeepsOnlyTheLoadBearingParentheses()
    {
        Assert.Equal("(subject:\"british\" OR description:\"british\") AND mediatype:movies",
            Query(WithKeywords("british", fields: SearchKeywordFields.SubjectsAndDescriptions)));
    }

    [Fact]
    public void Keywords_PhrasesAreMatchedAdjacently()
    {
        Assert.Equal("subject:\"country house\" AND mediatype:movies",
            Query(WithKeywords("\"country house\"")));
        Assert.Equal("(subject:\"Agatha Christie\" OR description:\"Agatha Christie\") AND mediatype:movies",
            Query(WithKeywords("\"Agatha Christie\"", fields: SearchKeywordFields.SubjectsAndDescriptions)));
    }

    [Fact]
    public void Keywords_UnquotedMultiwordEntryIsBroaderThanThePhrase()
    {
        // Documented limitation: unquoted multiword entry ANDs separate subject values.
        Assert.Equal("(subject:\"Agatha\" AND subject:\"Christie\") AND mediatype:movies",
            Query(WithKeywords("Agatha Christie")));
    }

    [Fact]
    public void Keywords_SubjectsMode_NeverMentionsDescription()
    {
        Assert.DoesNotContain("description", Query(WithKeywords("british mystery")), StringComparison.Ordinal);
    }

    [Fact]
    public void Keywords_ContainNoSynonymExpansion()
    {
        string query = Query(WithKeywords("british", fields: SearchKeywordFields.SubjectsAndDescriptions));

        // Exactly the two subject values and nothing else: no hidden broadening.
        Assert.Equal("(subject:\"british\" OR description:\"british\") AND mediatype:movies", query);
    }

    [Fact]
    public void Keywords_EmptyInput_EmitsNoClause()
    {
        string query = Query(WithKeywords("   "));

        // No keyword clause at all. The bare-scope leading space is pre-existing behaviour and is
        // unreachable in practice, because a criteria with no primary dimension cannot be searched.
        Assert.DoesNotContain("subject:", query, StringComparison.Ordinal);
    }

    [Fact]
    public void Keywords_ComposeWithExistingTitleGenreAndYear()
    {
        SearchCriteria criteria = WithKeywords("british", seed: Criteria("Miss Marple", yearFrom: 1959, yearTo: 1975))
            .WithGenres(new List<string> { "mystery" });

        // A single genre keeps the existing no-redundant-parentheses behaviour.
        Assert.Equal(
            "title:\"Miss Marple\" AND subject:\"british\" AND year:[1959 TO 1975] AND subject:\"mystery\" AND mediatype:movies",
            Query(criteria));
    }

    // --- Escaping / hostile input ------------------------------------------------------

    [Fact]
    public void Keywords_HostileTextStaysInsideOneLiteralPhrase()
    {
        // Bypassing the parser deliberately: the builder must still be safe if handed arbitrary text.
        SearchCriteria criteria = Criteria().WithKeywords(
            new List<string> { "mystery\") OR subject:\"british" },
            SearchKeywordFields.Subjects,
            SearchKeywordMatchMode.AllTerms);

        Assert.Equal("subject:\"mystery\\\") OR subject:\\\"british\" AND mediatype:movies", Query(criteria));
    }

    [Fact]
    public void Keywords_BackslashIsEscaped()
    {
        SearchCriteria criteria = Criteria().WithKeywords(
            new List<string> { "back\\slash" },
            SearchKeywordFields.Subjects,
            SearchKeywordMatchMode.AllTerms);

        Assert.Equal("subject:\"back\\\\slash\" AND mediatype:movies", Query(criteria));
    }

    [Fact]
    public void Exclusions_HostileTextStaysInsideOneLiteralPhrase()
    {
        // Bypassing the parser deliberately: the exclusions box rejects this input at the grammar
        // level, and the builder must still be safe if handed arbitrary text by any other caller.
        SearchCriteria criteria = WithKeywords("british").WithExcludedTerms(
            new List<string> { "horror\") OR subject:\"british" });

        Assert.Equal("subject:\"british\" AND NOT subject:\"horror\\\") OR subject:\\\"british\" AND mediatype:movies",
            Query(criteria));
    }

    [Fact]
    public void Exclusions_RejectHostileQuotedInputAtTheGrammarLevel()
    {
        Assert.False(SearchKeywordPhraseParser.TryParseTerms(
            "horror\") OR subject:\"british", out IReadOnlyList<string>? terms, out SearchKeywordParseError error));

        Assert.Null(terms);
        Assert.Equal(SearchKeywordParseError.EmbeddedQuote, error);
    }

    // --- Exclusion clauses -------------------------------------------------------------

    [Fact]
    public void Exclusions_Subjects_EmitNegatedFieldPhrasePerTerm()
    {
        Assert.Equal("subject:\"british\" AND NOT subject:\"horror\" AND NOT subject:\"news\" AND mediatype:movies",
            Query(WithExclusions("horror news", seed: WithKeywords("british"))));
    }

    [Fact]
    public void Exclusions_SubjectsAndDescriptions_NegateThePerFieldOr()
    {
        SearchCriteria criteria = WithKeywords("british", fields: SearchKeywordFields.SubjectsAndDescriptions)
            .WithExcludedTerms(new List<string> { "horror" });

        Assert.Equal("(subject:\"british\" OR description:\"british\") AND NOT (subject:\"horror\" OR description:\"horror\") AND mediatype:movies",
            Query(criteria));
    }

    [Fact]
    public void Exclusions_QuotedPhraseIsOneNegatedPhrase()
    {
        Assert.Equal("subject:\"british\" AND NOT subject:\"country house\" AND mediatype:movies",
            Query(WithExclusions("\"country house\"", seed: WithKeywords("british"))));
    }

    [Fact]
    public void Exclusions_NeverEmitAnOrGroupAtTheTopLevel()
    {
        string query = Query(WithExclusions("horror news documentary", seed: WithKeywords("british")));

        Assert.DoesNotContain("NOT (", query, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(query, "NOT subject:"));
    }

    [Fact]
    public void Exclusions_AreIndependentOfThePositiveMatchMode()
    {
        SearchCriteria any = WithExclusions("horror", seed: WithKeywords("british mystery", mode: SearchKeywordMatchMode.AnyTerms));
        SearchCriteria all = WithExclusions("horror", seed: WithKeywords("british mystery", mode: SearchKeywordMatchMode.AllTerms));

        Assert.Contains("NOT subject:\"horror\"", Query(any), StringComparison.Ordinal);
        Assert.Contains("NOT subject:\"horror\"", Query(all), StringComparison.Ordinal);
    }

    [Fact]
    public void Exclusions_EmptyInputEmitsNoClause()
    {
        Assert.Equal("subject:\"british\" AND mediatype:movies",
            Query(WithExclusions("  ", seed: WithKeywords("british"))));
    }

    // --- Video collections -------------------------------------------------------------

    private const string IncludeClause =
        "(collection:television OR collection:classic_tv OR collection:television_inbox)";

    private const string ExcludeClause =
        "NOT collection:television AND NOT collection:classic_tv AND NOT collection:television_inbox";

    [Fact]
    public void Collections_Default_EmitsNoClause()
    {
        Assert.Equal("subject:\"british\" AND mediatype:movies",
            Query(WithKeywords("british").WithVideoCollections(VideoCollectionScope.AllWatchableVideo)));
    }

    [Fact]
    public void Collections_Television_EmitsOrGroupOverTheSharedSet()
    {
        Assert.Equal("subject:\"british\" AND " + IncludeClause + " AND mediatype:movies",
            Query(WithKeywords("british").WithVideoCollections(VideoCollectionScope.TelevisionCollections)));
    }

    [Fact]
    public void Collections_ExcludeKnown_EmitsNegationsJoinedByAnd()
    {
        Assert.Equal("subject:\"british\" AND " + ExcludeClause + " AND mediatype:movies",
            Query(WithKeywords("british").WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections)));
    }

    [Fact]
    public void Collections_UseTheSameFixedSetForBothVariants()
    {
        IReadOnlyList<string> collections = SearchDiscoveryCatalog.TelevisionCollections();

        Assert.Equal(3, collections.Count);
        Assert.Contains("television", collections);
        Assert.Contains("classic_tv", collections);
        Assert.Contains("television_inbox", collections);

        foreach (string collection in collections)
        {
            string include = Query(WithKeywords("b").WithVideoCollections(VideoCollectionScope.TelevisionCollections));
            string exclude = Query(WithKeywords("b").WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections));

            Assert.Contains("collection:" + collection, include, StringComparison.Ordinal);
            Assert.Contains("collection:" + collection, exclude, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(SearchScope.WatchableVideo, " AND mediatype:movies")]
    [InlineData(SearchScope.RelatedMaterials, " AND NOT mediatype:movies")]
    [InlineData(SearchScope.Everything, "")]
    public void Collections_ComposeWithEveryPrimaryScope(SearchScope scope, string scopeSuffix)
    {
        foreach (VideoCollectionScope collections in SearchDisplayText.VideoCollectionOptions())
        {
            SearchCriteria criteria = WithKeywords("british", seed: Criteria(scope: scope))
                .WithVideoCollections(collections);

            string expected = "subject:\"british\"";
            string collectionClause = ExpectedCollectionClause(collections);
            if (collectionClause.Length > 0)
            {
                expected += " AND " + collectionClause;
            }

            Assert.Equal(expected + scopeSuffix, Query(criteria));
        }
    }

    [Fact]
    public void Collections_AreIndependentOfThePrimaryScopeEnum()
    {
        // The narrowing is a distinct type from SearchScope and composes without coupling.
        Assert.Equal("subject:\"b\" AND " + ExcludeClause + " AND NOT mediatype:movies",
            Query(WithKeywords("b", seed: Criteria(scope: SearchScope.RelatedMaterials))
                .WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections)));
    }

    private static string ExpectedCollectionClause(VideoCollectionScope scope)
    {
        return scope switch
        {
            VideoCollectionScope.TelevisionCollections => IncludeClause,
            VideoCollectionScope.ExcludeKnownTelevisionCollections => ExcludeClause,
            _ => ""
        };
    }

    // --- Full stack --------------------------------------------------------------------

    [Fact]
    public void FullStack_EmitsTheFixedClauseOrder()
    {
        SearchCriteria criteria = WithKeywords(
                "British mystery",
                seed: Criteria("Miss Marple", "Joan Hickson", 1959, 1975))
            .WithExcludedTerms(new List<string> { "horror" })
            .WithGenres(new List<string> { "mystery", "thriller" })
            .WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections)
            .WithExcludeTrailers(true)
            .WithOrdering(SearchOrdering.MostDownloaded);

        string expected =
            "title:\"Miss Marple\""
            + " AND (subject:\"British\" AND subject:\"mystery\")"
            + " AND NOT subject:\"horror\""
            + " AND creator:(Joan AND Hickson)"
            + " AND year:[1959 TO 1975]"
            + " AND (subject:\"mystery\" OR subject:\"thriller\")"
            + " AND " + ExcludeClause
            + " AND NOT collection:\"movie_trailers\" AND NOT subject:\"trailers\" AND NOT subject:\"trailer\""
            + " AND NOT (title:\"trailer\" OR title:\"trailers\")"
            + " AND mediatype:movies";

        Assert.Equal(expected, Query(criteria));
    }

    [Fact]
    public void FullStack_IsIndependentOfTheOrderCombinatorsWereApplied()
    {
        SearchCriteria a = Criteria("T", "C", 1950, 1955)
            .WithKeywords(new List<string> { "british" }, SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms)
            .WithExcludedTerms(new List<string> { "horror" })
            .WithGenres(new List<string> { "mystery" })
            .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
            .WithExcludeTrailers(true)
            .WithOrdering(SearchOrdering.MostDownloaded);

        SearchCriteria b = Criteria("T", "C", 1950, 1955)
            .WithExcludeTrailers(true)
            .WithOrdering(SearchOrdering.MostDownloaded)
            .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
            .WithGenres(new List<string> { "mystery" })
            .WithExcludedTerms(new List<string> { "horror" })
            .WithKeywords(new List<string> { "british" }, SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms);

        Assert.Equal(Query(a), Query(b));
    }

    [Fact]
    public void FullStack_TrailerExclusionStaysBeforeTheScopeClause()
    {
        string query = Query(WithKeywords("british")
            .WithExcludedTerms(new List<string> { "horror" })
            .WithExcludeTrailers(true));

        int trailers = query.IndexOf("movie_trailers", StringComparison.Ordinal);
        int media = query.IndexOf("mediatype:movies", StringComparison.Ordinal);

        Assert.True(trailers > 0);
        Assert.True(media > trailers);
    }

    // --- Ordering parameters -----------------------------------------------------------

    [Fact]
    public void Ordering_DefaultSendsNoSortParameters()
    {
        Assert.Empty(SearchDiscoveryCatalog.SortParametersFor(SearchOrdering.InternetArchiveDefault));
        Assert.False(SearchDiscoveryCatalog.HasSortParameters(SearchOrdering.InternetArchiveDefault));
    }

    [Fact]
    public void Ordering_MostDownloaded_SendsDownloadsThenIdentifierTiebreaker()
    {
        IReadOnlyList<string> sorts = SearchDiscoveryCatalog.SortParametersFor(SearchOrdering.MostDownloaded);

        Assert.Equal(2, sorts.Count);
        Assert.Equal("sort[]=downloads desc", sorts[0]);
        Assert.Equal("sort[]=identifier asc", sorts[1]);
    }

    // --- Lifecycle: paging, refresh, cancellation --------------------------------------

    [Fact]
    public async Task Paging_SendsIdenticalQueryAndSortOnEveryPage()
    {
        var handler = new RecordingHandler();
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        SearchCriteria criteria = WithKeywords("British mystery")
            .WithExcludedTerms(new List<string> { "horror" })
            .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
            .WithOrdering(SearchOrdering.MostDownloaded);

        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 3, 25));

        string first = handler.Uris[0].Query;
        string third = handler.Uris[1].Query;

        Assert.Equal(ExtractQ(first), ExtractQ(third));
        Assert.Equal(ExtractSorts(first), ExtractSorts(third));
        Assert.Contains("&page=1", first, StringComparison.Ordinal);
        Assert.Contains("&page=3", third, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ReissuesTheCompleteSpecificationIncludingOrdering()
    {
        var handler = new RecordingHandler();
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        SearchCriteria criteria = WithKeywords("British mystery")
            .WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections)
            .WithOrdering(SearchOrdering.MostDownloaded);

        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));

        Assert.Equal(handler.Uris[0].ToString(), handler.Uris[1].ToString());
    }

    [Fact]
    public async Task DefaultOrdering_SendsNoSortParameterOnTheWire()
    {
        var handler = new RecordingHandler();
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        await client.SearchAsync(new InternetArchiveSearchRequest(WithKeywords("british"), 1, 25));

        Assert.DoesNotContain("sort", handler.Uris[0].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MostDownloaded_SendsBothSortParametersEncoded()
    {
        var handler = new RecordingHandler();
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        SearchCriteria criteria = WithKeywords("british").WithOrdering(SearchOrdering.MostDownloaded);
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));

        string query = handler.Uris[0].Query;

        string downloadsParam = Uri.EscapeDataString("sort[]=downloads desc");
        string identifierParam = Uri.EscapeDataString("sort[]=identifier asc");

        int downloadsIndex = query.IndexOf(downloadsParam, StringComparison.Ordinal);
        int identifierIndex = query.IndexOf(identifierParam, StringComparison.Ordinal);

        Assert.True(downloadsIndex >= 0, "downloads sort parameter missing from: " + query);
        Assert.True(identifierIndex >= 0, "identifier tiebreaker missing from: " + query);
        Assert.True(downloadsIndex < identifierIndex, "downloads must precede identifier in: " + query);
        Assert.True(query.Contains("&" + downloadsParam, StringComparison.Ordinal), "sort parameter must be appended: " + query);
    }

    [Fact]
    public async Task Cancellation_StillPropagatesWithDiscoveryCriteria()
    {
        var client = new InternetArchiveApiClient(new HttpClient(new CancellingHandler()));

        SearchCriteria criteria = WithKeywords("british").WithOrdering(SearchOrdering.MostDownloaded);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25)));
    }

    [Fact]
    public void SharedCriteria_AreImmutableSoAConcurrentRequestCannotAlterThem()
    {
        SearchCriteria shared = WithKeywords("british").WithOrdering(SearchOrdering.MostDownloaded);
        string before = Query(shared);

        // The results window rebuilds a request from the same immutable instance per page.
        SearchCriteria withGenres = shared.WithGenres(new List<string> { "mystery" });
        SearchCriteria withOrdering = shared.WithOrdering(SearchOrdering.InternetArchiveDefault);

        Assert.Equal(before, Query(shared));
        Assert.NotEqual(before, Query(withGenres));

        // Ordering is a request parameter, not part of q, so it is compared separately.
        Assert.Equal(2, SearchDiscoveryCatalog.SortParametersFor(shared.Ordering).Count);
        Assert.Empty(SearchDiscoveryCatalog.SortParametersFor(withOrdering.Ordering));
    }

    [Fact]
    public void CompletedSearch_CarriesTheDiscoveryDimensionsForPaging()
    {
        SearchCriteria criteria = WithKeywords("British mystery")
            .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
            .WithOrdering(SearchOrdering.MostDownloaded);

        var completed = new CompletedSearch(criteria, 100, 1, new List<InternetArchiveSearchResult>());

        Assert.Same(criteria, completed.Criteria);
        Assert.Equal(2, completed.Criteria.Keywords.Count);
        Assert.Equal(VideoCollectionScope.TelevisionCollections, completed.Criteria.VideoCollections);
        Assert.Equal(SearchOrdering.MostDownloaded, completed.Criteria.Ordering);
    }

    [Fact]
    public async Task SupersededRequest_AgeLateResponseCannotReplaceTheNewerSpecificationsState()
    {
        // Out-of-order completion: the older request is released only AFTER the newer one has
        // already produced its response, modelling the window-supersede race.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new OutOfOrderHandler(gate);
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        SearchCriteria olderCriteria = WithKeywords("oldersearchterm")
            .WithOrdering(SearchOrdering.InternetArchiveDefault);
        SearchCriteria newerCriteria = WithKeywords("newersearchterm")
            .WithOrdering(SearchOrdering.MostDownloaded);

        Task<InternetArchiveSearchPage> olderTask =
            client.SearchAsync(new InternetArchiveSearchRequest(olderCriteria, 1, 25));
        Task<InternetArchiveSearchPage> newerTask =
            client.SearchAsync(new InternetArchiveSearchRequest(newerCriteria, 3, 25));

        // The newer request completes first and becomes the state the app keeps.
        InternetArchiveSearchPage newerPage = await newerTask;
        var state = new CompletedSearch(newerCriteria, newerPage.NumFound, 3, newerPage.Results);

        // Now the older request finally completes, carrying its own page and counts.
        gate.SetResult();
        InternetArchiveSearchPage olderPage = await olderTask;

        Assert.Equal("newer-doc", newerPage.Results[0].Identifier);
        Assert.Equal("older-doc", olderPage.Results[0].Identifier);

        // The newer specification is unaffected by the late older response...
        Assert.Same(newerCriteria, state.Criteria);
        Assert.Equal(3, state.Page);
        Assert.Equal(newerPage.NumFound, state.NumFound);
        Assert.Equal(SearchOrdering.MostDownloaded, state.Criteria.Ordering);
        Assert.DoesNotContain("oldersearchterm", Query(state.Criteria), StringComparison.Ordinal);

        // ...and the criteria used for the next page are still the newer ones.
        Assert.Equal(Query(newerCriteria), Query(state.Criteria));
        Assert.Equal(
            SearchDiscoveryCatalog.SortParametersFor(SearchOrdering.MostDownloaded).Count,
            SearchDiscoveryCatalog.SortParametersFor(state.Criteria.Ordering).Count);

        // The late response never mutates the shared immutable specification.
        Assert.Equal(
            Query(newerCriteria),
            Query(WithKeywords("newersearchterm")
                .WithOrdering(SearchOrdering.MostDownloaded)
                .WithVideoCollections(VideoCollectionScope.AllWatchableVideo)));
    }

    // --- Validity, defaults, reset -----------------------------------------------------

    [Fact]
    public void Validity_KeywordsAloneAreValid()
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear,
            "british mystery", null,
            SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.Valid, build.State);
        Assert.Equal(2, build.Criteria!.Keywords.Count);
    }

    [Fact]
    public void Validity_ExclusionsAloneAreValid()
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear,
            null, "horror",
            SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.Valid, build.State);
        Assert.Single(build.Criteria!.ExcludedTerms);
    }

    [Fact]
    public void Validity_FullyBlankIsRejected()
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear);

        Assert.Equal(CriteriaBuildState.NoCriteria, build.State);
        Assert.Null(build.Criteria);
    }

    [Fact]
    public void Validity_GenreAndTrailerAloneStillCannotStartASearch()
    {
        SearchCriteria onlyNarrowing = Criteria()
            .WithGenres(new List<string> { "mystery" })
            .WithExcludeTrailers(true);

        Assert.False(onlyNarrowing.HasAnyCriterion);

        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear);

        Assert.Equal(CriteriaBuildState.NoCriteria, build.State);
    }

    [Theory]
    [InlineData("\"country house")]
    [InlineData("country \"house")]
    [InlineData("\"\"")]
    [InlineData("un\"quoted")]
    [InlineData("\"a\"b")]
    public void Validation_RejectedKeywordEntryBlocksSubmission(string input)
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear,
            input, null,
            SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.InvalidKeywordEntry, build.State);
        Assert.Null(build.Criteria);
        Assert.False(string.IsNullOrWhiteSpace(build.Message));
    }

    [Theory]
    [InlineData(SearchKeywordMatchMode.AllTerms)]
    [InlineData(SearchKeywordMatchMode.AnyTerms)]
    [InlineData(SearchKeywordMatchMode.ExactPhrase)]
    public void Validation_UnterminatedQuoteIsRejectedInEveryMode(SearchKeywordMatchMode mode)
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear,
            "\"country house", null,
            SearchKeywordFields.Subjects, mode,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.InvalidKeywordEntry, build.State);
        Assert.Contains("Keywords", build.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_RejectedExclusionEntryBlocksSubmission()
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.WatchableVideo, CurrentYear,
            null, "\"country house",
            SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.InvalidKeywordEntry, build.State);
        Assert.Contains("Excluded terms", build.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_YearPrecedenceIsUnchanged()
    {
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            null, null, "12", null, SearchScope.WatchableVideo, CurrentYear,
            "\"unterminated", null,
            SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo, SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.InvalidYear, build.State);
    }

    [Fact]
    public void Validation_MessageNamesTheBoxAndTheCorrection()
    {
        string message = SearchDisplayText.KeywordEntryErrorText(
            "Keywords", SearchKeywordParseError.UnterminatedQuote);

        Assert.Contains("Keywords", message, StringComparison.Ordinal);
        Assert.Contains("closing quote", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_DefaultsProduceThePreDiscoveryQueryShape()
    {
        // The documented reset target for every Narrow-results control, with the primary title kept.
        CriteriaBuildResult build = FilterCriteriaLogic.BuildCriteria(
            "Miss Marple", null, null, null, SearchScope.WatchableVideo, CurrentYear,
            "", "",
            SearchKeywordFields.Subjects,
            SearchKeywordMatchMode.AllTerms,
            VideoCollectionScope.AllWatchableVideo,
            SearchOrdering.InternetArchiveDefault);

        Assert.Equal(CriteriaBuildState.Valid, build.State);
        Assert.Empty(build.Criteria!.Keywords);
        Assert.Empty(build.Criteria.ExcludedTerms);
        Assert.Equal(VideoCollectionScope.AllWatchableVideo, build.Criteria.VideoCollections);
        Assert.Equal(SearchOrdering.InternetArchiveDefault, build.Criteria.Ordering);
        Assert.Equal("title:\"Miss Marple\" AND mediatype:movies", Query(build.Criteria));
    }

    [Fact]
    public void Reset_IsIdempotent()
    {
        SearchCriteria once = Criteria("Miss Marple")
            .WithKeywords(new List<string> { "b" }, SearchKeywordFields.SubjectsAndDescriptions, SearchKeywordMatchMode.AnyTerms)
            .WithExcludedTerms(new List<string> { "x" })
            .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
            .WithOrdering(SearchOrdering.MostDownloaded)
            .WithGenres(new List<string> { "mystery" })
            .WithExcludeTrailers(true);

        SearchCriteria twice = once
            .WithKeywords(new List<string>(), SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms)
            .WithExcludedTerms(new List<string>())
            .WithVideoCollections(VideoCollectionScope.AllWatchableVideo)
            .WithOrdering(SearchOrdering.InternetArchiveDefault)
            .WithGenres(new List<string>())
            .WithExcludeTrailers(false);

        SearchCriteria third = twice
            .WithKeywords(new List<string>(), SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms)
            .WithExcludedTerms(new List<string>())
            .WithVideoCollections(VideoCollectionScope.AllWatchableVideo)
            .WithOrdering(SearchOrdering.InternetArchiveDefault)
            .WithGenres(new List<string>())
            .WithExcludeTrailers(false);

        Assert.Equal(Query(twice), Query(third));
        Assert.Equal("title:\"Miss Marple\" AND mediatype:movies", Query(twice));
    }

    [Fact]
    public void Defaults_OptionOrdersStartAtTheDocumentedDefaults()
    {
        Assert.Equal(SearchKeywordFields.Subjects, SearchDisplayText.KeywordFieldOptions()[0]);
        Assert.Equal(SearchKeywordMatchMode.AllTerms, SearchDisplayText.MatchModeOptions()[0]);
        Assert.Equal(VideoCollectionScope.AllWatchableVideo, SearchDisplayText.VideoCollectionOptions()[0]);
        Assert.Equal(SearchOrdering.InternetArchiveDefault, SearchDisplayText.OrderingOptions()[0]);
    }

    // --- Summaries ---------------------------------------------------------------------

    [Fact]
    public void Summary_LegacyTitleOnlyFormatIsUnchanged()
    {
        SearchCriteria criteria = Criteria("buckaroo");

        Assert.Equal("Query: \"buckaroo\" · 3 results · Page 1",
            SearchDisplayText.QuerySummaryText(criteria, 3, 1));
    }

    [Fact]
    public void Summary_LegacyCreatorAndYearFormatIsUnchanged()
    {
        SearchCriteria criteria = Criteria(null, "Ian Fleming", 1953, 1953);

        Assert.Equal("Query: creator \"Ian Fleming\", year 1953 · 7 results · Page 2",
            SearchDisplayText.QuerySummaryText(criteria, 7, 2));
    }

    [Fact]
    public void Summary_LegacyNoTitleNoCriteriaFormatIsUnchanged()
    {
        Assert.Equal("Query: criteria · 1 results · Page 1",
            SearchDisplayText.QuerySummaryText(Criteria(), 1, 1));
    }

    [Fact]
    public void Summary_AppendsDiscoverySegmentsToTheLegacyLabel()
    {
        SearchCriteria criteria = Criteria("Miss Marple")
            .WithKeywords(new List<string> { "British", "mystery" }, SearchKeywordFields.Subjects, SearchKeywordMatchMode.AllTerms)
            .WithExcludedTerms(new List<string> { "country house", "horror" })
            .WithVideoCollections(VideoCollectionScope.ExcludeKnownTelevisionCollections)
            .WithOrdering(SearchOrdering.MostDownloaded);

        string summary = SearchDisplayText.QuerySummaryText(criteria, 373, 1);

        Assert.StartsWith("Query: \"Miss Marple\"", summary, StringComparison.Ordinal);
        Assert.Contains("keywords British, mystery (All terms, Subjects)", summary, StringComparison.Ordinal);
        Assert.Contains("excluding \"country house\", horror", summary, StringComparison.Ordinal);
        Assert.Contains("Exclude known TV collections", summary, StringComparison.Ordinal);
        Assert.Contains("ordered by Most downloaded", summary, StringComparison.Ordinal);
        Assert.EndsWith(" · 373 results · Page 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_OmitsSegmentsForDefaultValuedDimensions()
    {
        string summary = SearchDisplayText.QuerySummaryText(Criteria("buckaroo"), 3, 1);

        Assert.DoesNotContain("keywords", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("All watchable video", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Internet Archive default", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_RendersKeywordsWithNoTitle()
    {
        SearchCriteria criteria = WithKeywords("British mystery");

        string summary = SearchDisplayText.QuerySummaryText(criteria, 373, 1);

        Assert.StartsWith("Query: criteria, keywords British, mystery", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_ActiveCountReportsNonDefaultDimensionsOnly()
    {
        Assert.Equal(0, SearchDisplayText.ActiveDiscoveryCount(Criteria("t")));
        Assert.Equal(1, SearchDisplayText.ActiveDiscoveryCount(WithKeywords("b")));
        Assert.Equal(1, SearchDisplayText.ActiveDiscoveryCount(WithExclusions("h")));
        Assert.Equal(1, SearchDisplayText.ActiveDiscoveryCount(
            Criteria().WithVideoCollections(VideoCollectionScope.TelevisionCollections)));
        Assert.Equal(1, SearchDisplayText.ActiveDiscoveryCount(
            Criteria().WithOrdering(SearchOrdering.MostDownloaded)));
        Assert.Equal(4, SearchDisplayText.ActiveDiscoveryCount(
            WithExclusions("h", seed: WithKeywords("b"))
                .WithVideoCollections(VideoCollectionScope.TelevisionCollections)
                .WithOrdering(SearchOrdering.MostDownloaded)));
    }

    // --- Honest-labelling text ---------------------------------------------------------

    [Fact]
    public void HelpText_DisclosesClassificationLimits()
    {
        Assert.Contains("no separate television media type", SearchDisplayText.VideoCollectionsHelp, StringComparison.Ordinal);
        Assert.Contains("not a complete Movies-only or Television-only", SearchDisplayText.VideoCollectionsHelp, StringComparison.Ordinal);
        Assert.Contains("Search scope", SearchDisplayText.VideoCollectionsHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpText_DisclosesPagingIsNotASnapshot()
    {
        Assert.Contains("not a frozen snapshot", SearchDisplayText.OrderingHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpText_DisclosesIncidentalMatches()
    {
        Assert.Contains("incidental", SearchDisplayText.KeywordFieldsHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpText_DisclosesQuoteRulesAndRejection()
    {
        Assert.Contains("unmatched quote", SearchDisplayText.PhraseGrammarHelp, StringComparison.Ordinal);
        Assert.Contains("rejected", SearchDisplayText.PhraseGrammarHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void Labels_AreHonestAndDistinct()
    {
        Assert.Equal("All watchable video", SearchDisplayText.VideoCollectionsLabel(VideoCollectionScope.AllWatchableVideo));
        Assert.Equal("TV collections", SearchDisplayText.VideoCollectionsLabel(VideoCollectionScope.TelevisionCollections));
        Assert.Equal("Exclude known TV collections", SearchDisplayText.VideoCollectionsLabel(VideoCollectionScope.ExcludeKnownTelevisionCollections));
        Assert.Equal("Internet Archive default", SearchDisplayText.OrderingLabel(SearchOrdering.InternetArchiveDefault));
        Assert.Equal("Most downloaded", SearchDisplayText.OrderingLabel(SearchOrdering.MostDownloaded));
    }

    // --- Local helpers -----------------------------------------------------------------

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static string ExtractQ(string query) => Segment(query, "q=");

    private static string ExtractSorts(string query)
    {
        var parts = new List<string>();
        foreach (string pair in query.TrimStart('?').Split('&'))
        {
            if (pair.StartsWith("sort", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(pair);
            }
        }
        return string.Join("&", parts);
    }

    private static string Segment(string query, string prefix)
    {
        foreach (string pair in query.TrimStart('?').Split('&'))
        {
            if (pair.StartsWith(prefix, StringComparison.Ordinal))
            {
                return pair;
            }
        }
        return "";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = new List<Uri>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":{\"numFound\":0,\"docs\":[]}}")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException();
        }
    }

    /// <summary>
    /// Holds the FIRST request open until <c>gate</c> is signalled, and answers every later request
    /// immediately. This produces a deterministic out-of-order completion: an older request
    /// finishes after a newer one, which is the race the launcher/results generation guard exists
    /// to survive.
    /// </summary>
    private sealed class OutOfOrderHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate;
        private int _seen;

        public OutOfOrderHandler(TaskCompletionSource gate) => _gate = gate;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bool isFirst = System.Threading.Interlocked.Increment(ref _seen) == 1;

            if (isFirst)
            {
                await _gate.Task.ConfigureAwait(false);
            }

            string doc = isFirst ? "older-doc" : "newer-doc";
            long numFound = isFirst ? 7 : 42;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"response\":{\"numFound\":" + numFound
                    + ",\"docs\":[{\"identifier\":\"" + doc + "\",\"title\":\"" + doc + "\"}]}}")
            };
        }
    }
}