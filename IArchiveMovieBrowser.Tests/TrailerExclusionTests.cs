using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser.Tests;

/// <summary>
/// Focused tests for the opt-in "Exclude trailers" narrow filter: the immutable criteria flag, and
/// the fixed four-clause exclusion expression the query builder emits for it.
///
/// The rule under test is deliberately aggressive (see <see cref="SearchCriteria.ExcludeTrailers"/>):
/// it excludes items by Internet Archive collection/subject metadata and by the presence of the word
/// "trailer" in the title. These tests assert what the query does, not what the Internet Archive
/// returns for it. They never issue a network request.
/// </summary>
public sealed class TrailerExclusionTests
{
    private const int CurrentYear = 2026;

    private static SearchCriteria Criteria(string title, SearchScope scope = SearchScope.Everything)
        => SearchCriteria.Validated(title, null, null, null, CurrentYear, scope);

    // --- Off by default ---------------------------------------------------------------

    [Fact]
    public void Criteria_TitleOnly_LeavesTrailerExclusionOff()
    {
        Assert.False(SearchCriteria.TitleOnly("Alien", SearchScope.Everything).ExcludeTrailers);
    }

    [Fact]
    public void Criteria_Validated_LeavesTrailerExclusionOff()
    {
        Assert.False(Criteria("Alien").ExcludeTrailers);
    }

    [Fact]
    public void Query_ExclusionOff_QueryIsUnchanged()
    {
        Assert.Equal("title:\"Alien\"", SearchScopeQueryBuilder.BuildQuery(Criteria("Alien")));
    }

    [Fact]
    public void Query_ExclusionOff_GenresAndScopeAreUnchanged()
    {
        SearchCriteria withGenres = Criteria("Alien", SearchScope.WatchableVideo)
            .WithGenres(GenreCatalog.NormalizeSelection(new List<string> { "horror" }));
        Assert.Equal("title:\"Alien\" AND subject:\"horror\" AND mediatype:movies",
            SearchScopeQueryBuilder.BuildQuery(withGenres));
    }

    // --- The four exclusion clauses ---------------------------------------------------

    [Fact]
    public void Query_ExclusionOn_EmitsAllFourClausesInOrder()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.Equal(
            "title:\"Alien\""
            + " AND NOT collection:\"movie_trailers\""
            + " AND NOT subject:\"trailers\""
            + " AND NOT subject:\"trailer\""
            + " AND NOT (title:\"trailer\" OR title:\"trailers\")",
            result);
    }

    [Fact]
    public void Query_ExclusionOn_SingularAndPluralSubjectClausesAreBothPresent()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.Contains("NOT subject:\"trailers\"", result);
        // The singular clause is a distinct token: the quoted plural must not swallow it.
        Assert.Equal(1, countOccurrences(result, "NOT subject:\"trailer\""));
        Assert.Equal(1, countOccurrences(result, "NOT subject:\"trailers\""));
    }

    [Fact]
    public void Query_ExclusionOn_UsesTheCuratedTrailerCollectionClause()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.Contains("NOT collection:\"movie_trailers\"", result);
    }

    [Fact]
    public void Query_ExclusionOn_TitleRuleCoversSingularAndPlural()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.Contains("NOT (title:\"trailer\" OR title:\"trailers\")", result);
    }

    [Fact]
    public void Query_ExclusionOn_OmitsRedundantSubcollectionClauses()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.DoesNotContain("movie_trailers_unsorted", result);
        Assert.DoesNotContain("movie_trailers_picfixer", result);
    }

    [Fact]
    public void Query_ExclusionOn_MatchesNoDescriptionFieldAndNoRuntimeHeuristic()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien").WithExcludeTrailers(true));

        Assert.DoesNotContain("description", result);
        Assert.DoesNotContain("runtime", result);
    }

    [Fact]
    public void Query_ExclusionOn_UsesOnlyLiteralAppControlledTerms()
    {
        // The clause text is a constant, so arbitrary user title text can never leak operator syntax
        // into it. This guards the escaping invariant the rest of the builder depends on.
        string hostile = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien\") OR collection:\"movie_trailers").WithExcludeTrailers(true));

        Assert.Contains("NOT collection:\"movie_trailers\"", hostile);
        Assert.Equal(1, countOccurrences(hostile, "NOT collection:\"movie_trailers\""));
    }

    // --- Combination with the other filters -------------------------------------------

    [Fact]
    public void Query_ExclusionOn_ComesAfterTitlePhrase()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("War of the Worlds").WithExcludeTrailers(true));

        Assert.StartsWith("title:\"War of the Worlds\" AND NOT", result);
    }

    [Fact]
    public void Query_ExclusionOn_ComesAfterCreatorAndYearClauses()
    {
        var build = FilterCriteriaLogic.BuildCriteria(
            null, "Gene Barry", "1950", "1960", SearchScope.Everything, CurrentYear);

        string result = SearchScopeQueryBuilder.BuildQuery(
            build.Criteria!.WithExcludeTrailers(true));

        Assert.Equal(
            "creator:(Gene AND Barry) AND year:[1950 TO 1960]"
            + " AND NOT collection:\"movie_trailers\""
            + " AND NOT subject:\"trailers\""
            + " AND NOT subject:\"trailer\""
            + " AND NOT (title:\"trailer\" OR title:\"trailers\")",
            result);
    }

    [Fact]
    public void Query_ExclusionOn_ComesAfterGenreClauseAndKeepsTheOrGroupIntact()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(
            new List<string> { "science-fiction", "war" });

        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("War of the Worlds").WithGenres(selected).WithExcludeTrailers(true));

        Assert.Equal(
            "title:\"War of the Worlds\" AND (subject:\"science fiction\" OR subject:\"war\")"
            + " AND NOT collection:\"movie_trailers\""
            + " AND NOT subject:\"trailers\""
            + " AND NOT subject:\"trailer\""
            + " AND NOT (title:\"trailer\" OR title:\"trailers\")",
            result);
    }

    [Fact]
    public void Query_ExclusionOn_PrecedesTheWatchableVideoScopeCondition()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien", SearchScope.WatchableVideo).WithExcludeTrailers(true));

        Assert.EndsWith(
            "AND NOT (title:\"trailer\" OR title:\"trailers\") AND mediatype:movies", result);
    }

    [Fact]
    public void Query_ExclusionOn_PreservesTheRelatedMaterialsScopeCondition()
    {
        string result = SearchScopeQueryBuilder.BuildQuery(
            Criteria("Alien", SearchScope.RelatedMaterials).WithExcludeTrailers(true));

        Assert.EndsWith(
            "AND NOT (title:\"trailer\" OR title:\"trailers\") AND NOT mediatype:movies", result);
    }

    [Fact]
    public void Query_ExclusionOn_AllFiltersTogetherAreDeterministic()
    {
        var build = FilterCriteriaLogic.BuildCriteria(
            "War of the Worlds", "Gene Barry", "1950", "1960",
            SearchScope.WatchableVideo, CurrentYear);
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(
            new List<string> { "war", "science-fiction" });

        string first = SearchScopeQueryBuilder.BuildQuery(
            build.Criteria!.WithGenres(selected).WithExcludeTrailers(true));
        string second = SearchScopeQueryBuilder.BuildQuery(
            build.Criteria!.WithGenres(selected).WithExcludeTrailers(true));

        Assert.Equal(first, second);
        Assert.Equal(
            "title:\"War of the Worlds\" AND creator:(Gene AND Barry) AND year:[1950 TO 1960]"
            + " AND (subject:\"science fiction\" OR subject:\"war\")"
            + " AND NOT collection:\"movie_trailers\""
            + " AND NOT subject:\"trailers\""
            + " AND NOT subject:\"trailer\""
            + " AND NOT (title:\"trailer\" OR title:\"trailers\")"
            + " AND mediatype:movies",
            first);
    }

    // --- Criteria state: composition and validity --------------------------------------

    [Fact]
    public void WithExcludeTrailers_PreservesEveryOtherCriterion()
    {
        var build = FilterCriteriaLogic.BuildCriteria(
            "Alien", "Ridley Scott", "1979", "1980",
            SearchScope.WatchableVideo, CurrentYear);
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "horror" });
        SearchCriteria before = build.Criteria!.WithGenres(selected);

        SearchCriteria after = before.WithExcludeTrailers(true);

        Assert.True(after.ExcludeTrailers);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Creator, after.Creator);
        Assert.Equal(before.YearFrom, after.YearFrom);
        Assert.Equal(before.YearTo, after.YearTo);
        Assert.Equal(before.YearMax, after.YearMax);
        Assert.Equal(before.Scope, after.Scope);
        Assert.Equal(before.SelectedGenres, after.SelectedGenres);
        Assert.Equal(before.HasAnyCriterion, after.HasAnyCriterion);
        Assert.Equal(before.IsExactYear, after.IsExactYear);
    }

    [Fact]
    public void WithExcludeTrailers_ComposesInEitherOrderWithWithGenres()
    {
        IReadOnlyList<string> selected = GenreCatalog.NormalizeSelection(new List<string> { "horror" });

        SearchCriteria genresThenFlag = Criteria("Alien")
            .WithGenres(selected)
            .WithExcludeTrailers(true);
        SearchCriteria flagThenGenres = Criteria("Alien")
            .WithExcludeTrailers(true)
            .WithGenres(selected);

        Assert.Equal(
            SearchScopeQueryBuilder.BuildQuery(genresThenFlag),
            SearchScopeQueryBuilder.BuildQuery(flagThenGenres));
    }

    [Fact]
    public void WithGenres_PreservesTheTrailerExclusionFlag()
    {
        SearchCriteria flagged = Criteria("Alien").WithExcludeTrailers(true);
        Assert.True(flagged.WithGenres(new List<string> { "horror" }).ExcludeTrailers);
    }

    [Fact]
    public void WithExcludeTrailers_TurnedBackOff_RemovesEveryClause()
    {
        SearchCriteria on = Criteria("Alien").WithExcludeTrailers(true);
        string off = SearchScopeQueryBuilder.BuildQuery(on.WithExcludeTrailers(false));

        Assert.Equal("title:\"Alien\"", off);
    }

    [Fact]
    public void WithExcludeTrailers_DoesNotMakeABlankSearchValid()
    {
        // Trailer exclusion is a narrowing option, never a search criterion in its own right.
        var build = FilterCriteriaLogic.BuildCriteria(
            null, null, null, null, SearchScope.Everything, CurrentYear);

        // A blank search is rejected before any criteria object exists, so the toggle can never
        // become the reason a search runs.
        Assert.Equal(CriteriaBuildState.NoCriteria, build.State);
        Assert.Null(build.Criteria);

        // And even if a criteria object carrying the flag is constructed directly, it stays invalid.
        SearchCriteria flaggedOnly = SearchCriteria.Validated(
            null, null, null, null, CurrentYear, SearchScope.Everything).WithExcludeTrailers(true);
        Assert.True(flaggedOnly.ExcludeTrailers);
        Assert.False(flaggedOnly.HasAnyCriterion);
        Assert.Throws<ArgumentException>(
            () => new InternetArchiveSearchRequest(flaggedOnly, 1, 25));
    }

    [Fact]
    public void CompletedSearch_CarriesTheTrailerExclusionThroughToPaging()
    {
        var build = FilterCriteriaLogic.BuildCriteria(
            "Alien", null, null, null, SearchScope.WatchableVideo, CurrentYear);
        SearchCriteria flagged = build.Criteria!.WithExcludeTrailers(true);

        var completed = new CompletedSearch(
            flagged, 7, 1, new List<InternetArchiveSearchResult>());

        Assert.True(completed.Criteria.ExcludeTrailers);
        // Refresh and paging rebuild a request from these criteria, so the filter stays applied.
        var refreshRequest = new InternetArchiveSearchRequest(completed.Criteria, 2, 25);
        Assert.Contains(
            "NOT collection:\"movie_trailers\"",
            SearchScopeQueryBuilder.BuildQuery(refreshRequest.Criteria));
    }

    private static int countOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
        }
        return count;
    }
}