using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the optional Genre narrow filter: the curated genre catalog/model, the
/// Internet Archive <c>subject</c> query construction (single, OR-group, AND-combined with other
/// filters, deduplicated, deterministic order), and the UI-independent selection/state behavior.
/// No test performs a live Internet Archive network request.
/// </summary>
public sealed class GenreFilterTests
{
    private const int CurrentYear = 1980; // accepted max is 1981

    // --- Genre catalog / model -------------------------------------------------------

    [Fact]
    public void Catalog_ContainsExactly16CuratedChoices()
    {
        Assert.Equal(16, GenreCatalog.All().Count);
    }

    [Fact]
    public void Catalog_LabelsInExactDeclaredOrder()
    {
        var expected = new List<string>
        {
            "Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama",
            "Family", "Fantasy", "Horror", "Mystery", "Romance", "Science fiction", "Thriller",
            "War", "Western"
        };
        Assert.Equal(expected.Count, GenreCatalog.All().Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], GenreCatalog.All()[i].Label);
        }
    }

    [Fact]
    public void Catalog_KeysPhrasesAndOrderAreExact()
    {
        var expectedLabels = new List<string>
        {
            "Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama",
            "Family", "Fantasy", "Horror", "Mystery", "Romance", "Science fiction", "Thriller",
            "War", "Western"
        };
        var expectedPhrases = new List<string>
        {
            "action", "adventure", "animation", "comedy", "crime", "documentary", "drama",
            "family", "fantasy", "horror", "mystery", "romance", "science fiction", "thriller",
            "war", "western"
        };
        for (int i = 0; i < expectedPhrases.Count; i++)
        {
            GenreDefinition genre = GenreCatalog.All()[i];
            Assert.Equal(expectedLabels[i], genre.Label);
            Assert.Equal(expectedPhrases[i], genre.SubjectPhrase);
            Assert.False(string.IsNullOrWhiteSpace(genre.Key));
        }
    }

    [Fact]
    public void Catalog_NoDuplicateThemeKeys()
    {
        var seen = new List<string>();
        foreach (GenreDefinition genre in GenreCatalog.All())
        {
            Assert.DoesNotContain(genre.Key, seen);
            seen.Add(genre.Key);
        }
    }

    [Fact]
    public void Catalog_ScienceFiction_MapsToMultiwordPhrase()
    {
        Assert.Equal("science fiction", GenreCatalog.SubjectPhraseFor("science-fiction"));
    }

    [Fact]
    public void Catalog_UnknownKey_ResolvesToNull()
    {
        Assert.Null(GenreCatalog.SubjectPhraseFor("not-a-genre"));
        Assert.False(GenreCatalog.IsKnownKey("custom"));
    }

    [Fact]
    public void Catalog_Deterministic_IndependentOfClickOrder()
    {
        IReadOnlyList<string> ordered = GenreCatalog.NormalizeSelection(
            new List<string> { "horror", "western", "science-fiction", "war" });
        // Declared order: horror < science-fiction < war < western.
        Assert.Equal(4, ordered.Count);
        Assert.Equal("horror", ordered[0]);
        Assert.Equal("science-fiction", ordered[1]);
        Assert.Equal("war", ordered[2]);
        Assert.Equal("western", ordered[3]);
    }

    [Fact]
    public void Catalog_NoLabelIsUsedAsUnvalidatedRawQueryClause()
    {
        // Every subject phrase must come from the centralized mapping and be inert literal text:
        // equal to the lowercase label, containing no Lucene/IA query syntax that could act as a
        // field, operator, wildcard, group, range, or modifier.
        foreach (GenreDefinition genre in GenreCatalog.All())
        {
            string phrase = genre.SubjectPhrase;
            Assert.Equal(genre.Label.ToLowerInvariant(), phrase);
            for (int i = 0; i < phrase.Length; i++)
            {
                char c = phrase[i];
                bool reserved = c == ':' || c == '\"' || c == '\\' || c == '(' || c == ')'
                    || c == '[' || c == ']' || c == '{' || c == '}' || c == '*' || c == '?'
                    || c == '+' || c == '-' || c == '!' || c == '^' || c == '~' || c == '/'
                    || c == '&' || c == '|';
                Assert.False(reserved, "subject phrase must be inert literal text: " + phrase);
            }
        }
    }

    // --- Query construction: no genre / single genre ---------------------------------

    [Fact]
    public void Query_NoGenres_HasNoSubjectClauseAndIsUnchanged()
    {
        var criteria = Criteria("War of the Worlds", SearchScope.WatchableVideo);
        string noGenres = SearchScopeQueryBuilder.BuildQuery(criteria);
        string withEmptyGenres = SearchScopeQueryBuilder.BuildQuery(criteria.WithGenres(new List<string>()));
        Assert.Equal(noGenres, withEmptyGenres);
        Assert.Equal("title:\"War of the Worlds\" AND mediatype:movies", withEmptyGenres);
        Assert.DoesNotContain("subject:", withEmptyGenres);
    }

    [Fact]
    public void Query_ScienceFiction_SingleQuotedSubjectPhrase()
    {
        // Science fiction -> subject:"science fiction"
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "science-fiction" });
        string result = SearchScopeQueryBuilder.BuildQuery(Criteria("A Name", SearchScope.Everything).WithGenres(selected));
        Assert.Equal("title:\"A Name\" AND subject:\"science fiction\"", result);
    }

    [Fact]
    public void Query_Western_SingleSubjectClause()
    {
        // Western -> subject:"western"
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Rio Bravo", SearchScope.Everything).WithGenres(new List<string> { "western" }));
        Assert.Equal("title:\"Rio Bravo\" AND subject:\"western\"", result);
    }

    [Fact]
    public void Query_GenreOnlyWithinValidSearch_HasNoRedundantParens()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("x", SearchScope.Everything).WithGenres(new List<string> { "comedy" }));
        Assert.Equal("title:\"x\" AND subject:\"comedy\"", result);
    }

    // --- Query construction: multiple genres (OR group) -------------------------------

    [Fact]
    public void Query_HorrorPlusMystery_ParenthesizedOrGroupInDeclaredOrder()
    {
        // Horror + Mystery -> (subject:"horror" OR subject:"mystery")
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "mystery", "horror" });
        Assert.Equal(2, selected.Count);
        Assert.Equal("horror", selected[0]);
        Assert.Equal("mystery", selected[1]);

        string result = SearchScopeQueryBuilder.BuildQuery(Criteria("T", SearchScope.Everything).WithGenres(selected));
        Assert.Equal("title:\"T\" AND (subject:\"horror\" OR subject:\"mystery\")", result);
    }

    [Fact]
    public void Query_MultipleGenres_NeverFormAndClauseBetweenGenreValues()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "horror", "mystery" });
        string result = SearchScopeQueryBuilder.BuildQuery(Criteria("T", SearchScope.Everything).WithGenres(selected));
        Assert.DoesNotContain("subject:\"horror\" AND subject:\"mystery\"", result);
        Assert.Contains("(subject:\"horror\" OR subject:\"mystery\")", result);
    }

    [Fact]
    public void Query_DuplicateSelections_DoNotCreateDuplicateClauses()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "western", "western", "war", "war" });
        Assert.Equal(2, selected.Count);
        string result = SearchScopeQueryBuilder.BuildQuery(Criteria("T", SearchScope.Everything).WithGenres(selected));
        Assert.Equal("title:\"T\" AND (subject:\"war\" OR subject:\"western\")", result);
        Assert.Equal(1, countOccurrences(result, "subject:\"war\""));
        Assert.Equal(1, countOccurrences(result, "subject:\"western\""));
    }

    [Fact]
    public void Query_ThreeGenres_ParenthesizedMultiWayOr()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "comedy", "drama", "family" });
        string result = SearchScopeQueryBuilder.BuildQuery(Criteria("T", SearchScope.Everything).WithGenres(selected));
        Assert.Equal("title:\"T\" AND (subject:\"comedy\" OR subject:\"drama\" OR subject:\"family\")", result);
    }

    // --- Query construction: AND combination with other filters ---------------------

    [Fact]
    public void Query_GenresCombineWithTitlePhrase_UsingAnd()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("War of the Worlds", SearchScope.Everything).WithGenres(new List<string> { "science-fiction", "war" }));
        Assert.Equal("title:\"War of the Worlds\" AND (subject:\"science fiction\" OR subject:\"war\")", result);
    }

    [Fact]
    public void Query_GenresCombineWithCreator_UsingAnd()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, "Gene Barry", null, null, SearchScope.Everything, CurrentYear);
        string result = SearchScopeQueryBuilder.BuildQuery(r.Criteria!.WithGenres(new List<string> { "western" }));
        Assert.Equal("creator:(Gene AND Barry) AND subject:\"western\"", result);
    }

    [Fact]
    public void Query_GenresCombineWithExactYear_UsingAnd()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1953", "1953", SearchScope.Everything, CurrentYear);
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "war" });
        string result = SearchScopeQueryBuilder.BuildQuery(r.Criteria!.WithGenres(selected));
        Assert.Equal("year:1953 AND subject:\"war\"", result);
    }

    [Fact]
    public void Query_GenresCombineWithYearRange_UsingAnd()
    {
        var r = FilterCriteriaLogic.BuildCriteria(null, null, "1950", "1960", SearchScope.Everything, CurrentYear);
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "war", "documentary" });
        string result = SearchScopeQueryBuilder.BuildQuery(r.Criteria!.WithGenres(selected));
        // declared order: documentary < war
        Assert.Equal("year:[1950 TO 1960] AND (subject:\"documentary\" OR subject:\"war\")", result);
    }

    [Fact]
    public void Query_GenresCombineWithScope_UsingAnd()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "horror", "mystery" });
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Curse", SearchScope.WatchableVideo).WithGenres(selected));
        Assert.Equal("title:\"Curse\" AND (subject:\"horror\" OR subject:\"mystery\") AND mediatype:movies", result);
    }

    [Fact]
    public void Query_AllFiltersTogether_AreDeterministicAndCorrectlyParenthesized()
    {
        var r = FilterCriteriaLogic.BuildCriteria("War of the Worlds", "Gene Barry", "1950", "1960",
            SearchScope.WatchableVideo, CurrentYear);
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(
            new List<string> { "western", "science-fiction", "war" });
        string result = SearchScopeQueryBuilder.BuildQuery(r.Criteria!.WithGenres(selected));

        string expected = "title:\"War of the Worlds\" AND creator:(Gene AND Barry)"
            + " AND year:[1950 TO 1960]"
            + " AND (subject:\"science fiction\" OR subject:\"war\" OR subject:\"western\")"
            + " AND mediatype:movies";
        Assert.Equal(expected, result);
    }

    // --- Search state / UI-independent behavior ----------------------------------------

    [Fact]
    public void Request_ConstructedFromGenreSelection_CarriesStableGenreValues()
    {
        var state = new GenreSelectionState();
        state.Select("mystery");
        state.Select("horror");

        var request = new InternetArchiveSearchRequest(
            Criteria("T", SearchScope.WatchableVideo).WithGenres(state.SelectedKeys()), 1, 25);
        Assert.Equal(2, request.Criteria.SelectedGenres.Count);
        Assert.Equal("horror", request.Criteria.SelectedGenres[0]);
        Assert.Equal("mystery", request.Criteria.SelectedGenres[1]);
    }

    [Fact]
    public void Request_NewSearchAfterGenreSelection_UsesPage1()
    {
        var state = new GenreSelectionState();
        state.Select("comedy");
        // A freshly launched search defaults to page 1 (the launcher always builds page 1).
        var request = new InternetArchiveSearchRequest(
            Criteria("T", SearchScope.Everything).WithGenres(state.SelectedKeys()));
        Assert.Equal(1, request.Page);
        Assert.Equal("comedy", request.Criteria.SelectedGenres[0]);
    }

    [Fact]
    public void Request_PagingRetainsSelectedGenres()
    {
        IReadOnlyList<string> selected = new List<string> { "war", "documentary" };
        var criteria = Criteria("T", SearchScope.WatchableVideo).WithGenres(selected);

        var page1 = new InternetArchiveSearchRequest(criteria, 1, 25);
        var page3 = new InternetArchiveSearchRequest(criteria, 3, 25);
        Assert.Equal(2, page1.Criteria.SelectedGenres.Count);
        Assert.Equal(page1.Criteria.SelectedGenres[0], page3.Criteria.SelectedGenres[0]);
        Assert.Equal(page1.Criteria.SelectedGenres[1], page3.Criteria.SelectedGenres[1]);
    }

    [Fact]
    public void CompletedSearch_RefreshCarriesSelectedGenres()
    {
        IReadOnlyList<string> selected = new List<string> { "science-fiction", "war" };
        var criteria = Criteria("War of the Worlds", SearchScope.WatchableVideo).WithGenres(selected);
        var completed = new CompletedSearch(criteria, 42, 1, new List<InternetArchiveSearchResult>());
        // Refresh reuses the completed criteria, so the genre filter is retained.
        Assert.Equal(2, completed.Criteria.SelectedGenres.Count);
        Assert.Equal("science-fiction", completed.Criteria.SelectedGenres[0]);
        Assert.Equal("war", completed.Criteria.SelectedGenres[1]);
    }

    [Fact]
    public void ClearGenres_EmptiesOnlyTheGenreSelection()
    {
        var state = new GenreSelectionState();
        state.Select("horror");
        state.Select("western");
        Assert.Equal(2, state.SelectedCount());

        state.Clear();

        Assert.Equal(0, state.SelectedCount());
        Assert.Empty(state.SelectedKeys());
    }

    [Fact]
    public void ClearGenres_DoesNotAlterTitleScopeCreatorOrYear()
    {
        var r = FilterCriteriaLogic.BuildCriteria("War of the Worlds", "Gene Barry", "1950", "1955",
            SearchScope.WatchableVideo, CurrentYear);
        var withGenres = r.Criteria!.WithGenres(new List<string> { "war" });
        var cleared = withGenres.WithGenres(new List<string>());

        Assert.Equal("War of the Worlds", cleared.Title);
        Assert.Equal("Gene Barry", cleared.Creator);
        Assert.Equal(1950, cleared.YearFrom);
        Assert.Equal(1955, cleared.YearTo);
        Assert.Equal(SearchScope.WatchableVideo, cleared.Scope);
        Assert.Empty(cleared.SelectedGenres);
        Assert.Equal(withGenres.HasAnyCriterion, cleared.HasAnyCriterion);
    }

    [Fact]
    public void GenreSelectionState_ChangeDoesNotConstructAnyRequest()
    {
        // The selection model is a plain key tracker with no network/query dependency: selecting
        // genres by itself produces no query text and no request (Search/Refresh trigger requests).
        var state = new GenreSelectionState();
        state.Select("horror");
        state.Select("mystery");
        Assert.Equal(2, state.SelectedCount());
        // Only immutable ordered stable keys are exposed; never a raw q= expression.
        Assert.False(anyContainsQuerySyntax(state.SelectedKeys()));
    }

    [Fact]
    public void SearchDisplayText_GenreHeaderReflectsActiveSelectionAsAccessibleText()
    {
        Assert.Equal("Genre", SearchDisplayText.GenreSectionHeader(0));
        Assert.Equal("Genre (1 selected)", SearchDisplayText.GenreSectionHeader(1));
        Assert.Equal("Genre (3 selected)", SearchDisplayText.GenreSectionHeader(3));
    }

    // --- Client-level paging retains genre clauses -----------------------------------

    [Fact]
    public async Task Client_PagingRetainsGenreQuery()
    {
        const string json = "{\"response\":{\"numFound\":2,\"docs\":[{\"identifier\":\"id\"}]}}";
        var handler = new FakeHandler(req => JsonResponse(HttpStatusCode.OK, json));
        var client = new InternetArchiveApiClient(new HttpClient(handler));

        IReadOnlyList<string> selected = new List<string> { "comedy", "western" };
        var criteria = Criteria("T", SearchScope.WatchableVideo).WithGenres(selected);
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 1, 25));
        await client.SearchAsync(new InternetArchiveSearchRequest(criteria, 3, 25));

        Assert.Equal(2, handler.Requests.Count);
        string expression = Uri.EscapeDataString(
            "title:\"T\" AND (subject:\"comedy\" OR subject:\"western\") AND mediatype:movies");
        Assert.Contains("q=" + expression + "&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("q=" + expression + "&", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("&page=1&", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("&page=3&", handler.Requests[1].RequestUri!.Query);
    }

    // --- Helpers -------------------------------------------------------------------------

    private static SearchCriteria Criteria(string? title, SearchScope scope)
    {
        var r = FilterCriteriaLogic.BuildCriteria(title, null, null, null, scope, CurrentYear);
        Assert.Equal(CriteriaBuildState.Valid, r.State);
        return r.Criteria!;
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

    private static bool anyContainsQuerySyntax(IReadOnlyList<string> keys)
    {
        foreach (string key in keys)
        {
            string? phrase = GenreCatalog.SubjectPhraseFor(key);
            if (phrase is not null && (phrase.Contains("subject:") || phrase.Contains("\"")))
            {
                return true;
            }
        }
        return false;
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