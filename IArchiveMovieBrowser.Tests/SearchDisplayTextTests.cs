using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class SearchDisplayTextTests
{
    private static InternetArchiveSearchResult Result(
        string? identifier,
        string? title,
        System.Collections.Generic.IReadOnlyList<string>? creators,
        string? date,
        string? year,
        string? mediaType)
    {
        return new InternetArchiveSearchResult(
            identifier ?? "item",
            title,
            creators,
            date,
            year,
            mediaType,
            null,
            null);
    }

    [Fact]
    public void RowText_FormatsTwoCompactLines()
    {
        var r = Result("i1", "Big Buck", new List<string> { "A", "B" }, null, "2000", "movies");

        Assert.Equal("Big Buck\n2000 · A, B · movies", SearchDisplayText.RowText(r));
    }

    [Fact]
    public void TitleText_FallsBackWhenMissing()
    {
        Assert.Equal("(untitled)", SearchDisplayText.TitleText(Result("i", null, null, null, null, null)));
        Assert.Equal("(untitled)", SearchDisplayText.TitleText(Result("i", "   ", null, null, null, null)));
    }

    [Fact]
    public void SecondLine_FallsBackForEachMissingField()
    {
        var r = Result("i", "X", null, null, null, null);

        Assert.Equal("(no date) · (unknown creator) · (unknown type)", SearchDisplayText.SecondLineText(r));
    }

    [Fact]
    public void DateYear_PrefersDateThenYear()
    {
        Assert.Equal("2021-05-01", SearchDisplayText.DateYearText(Result("i", "X", null, "2021-05-01", "2021", null)));
        Assert.Equal("1999", SearchDisplayText.DateYearText(Result("i", "X", null, null, "1999", null)));
        Assert.Equal("(no date)", SearchDisplayText.DateYearText(Result("i", "X", null, null, null, null)));
    }

    [Fact]
    public void Creators_JoinsMultipleAndFallsBack()
    {
        Assert.Equal("A, B, C", SearchDisplayText.CreatorsText(Result("i", "X", new List<string> { "A", "B", "C" }, null, null, null)));
        Assert.Equal("(unknown creator)", SearchDisplayText.CreatorsText(Result("i", "X", null, null, null, null)));
        Assert.Equal("(unknown creator)", SearchDisplayText.CreatorsText(Result("i", "X", new List<string>(), null, null, null)));
    }

    [Fact]
    public void ResultsSummary_IncludesCountAndPage()
    {
        Assert.Equal("128 results · Page 2", SearchDisplayText.ResultsSummary(128, 2));
        Assert.Equal("0 results · Page 1", SearchDisplayText.ResultsSummary(0, 1));
    }

    [Fact]
    public void Status_MessagesAreReadable()
    {
        Assert.Equal("Loading…", SearchDisplayText.StatusLoading());
        Assert.Equal("Ready — showing page 3.", SearchDisplayText.StatusReady(3));
        Assert.Equal("No results.", SearchDisplayText.StatusNoResults());
        Assert.Equal("Cancelled.", SearchDisplayText.StatusCancelled());
        Assert.Equal("Error: boom", SearchDisplayText.StatusError("boom"));
        Assert.Equal("Enter a search term.", SearchDisplayText.EmptyQueryText());
    }

    [Fact]
    public void Pagination_ButtonStates()
    {
        Assert.False(SearchDisplayText.PreviousEnabled(1));
        Assert.True(SearchDisplayText.PreviousEnabled(2));

        Assert.False(SearchDisplayText.NextEnabled(25, 1, 25));
        Assert.True(SearchDisplayText.NextEnabled(26, 1, 25));
        Assert.True(SearchDisplayText.NextEnabled(100, 3, 25));
        Assert.False(SearchDisplayText.NextEnabled(100, 4, 25));
        Assert.False(SearchDisplayText.NextEnabled(0, 1, 25));
    }

    [Fact]
    public void Launcher_ConstantsAreExact()
    {
        Assert.Equal("Search Internet Archive", SearchDisplayText.LauncherTitleText);
        Assert.Equal(
            "Search public Internet Archive movie and video titles.",
            SearchDisplayText.LauncherInstructionText);
        Assert.Equal("No Results", SearchDisplayText.NoResultsLabelText);
    }

    [Theory]
    [InlineData(0, "Open Results (0)")]
    [InlineData(1, "Open Results (1)")]
    [InlineData(132, "Open Results (132)")]
    public void Launcher_OpenResultsLabel(long numFound, string expected)
    {
        Assert.Equal(expected, SearchDisplayText.OpenResultsText(numFound));
    }

    [Fact]
    public void Launcher_StatusTexts()
    {
        Assert.Equal("Searching Internet Archive…", SearchDisplayText.SearchingText());
        Assert.Equal("Search complete — 132 matching items found.", SearchDisplayText.FoundText(132));
        Assert.Equal("No matching Internet Archive items found.", SearchDisplayText.NoMatchingItemsText());
        Assert.Equal("Enter a search term.", SearchDisplayText.EmptyQueryText());
    }

    [Fact]
    public void Launcher_QuerySummaryText()
    {
        Assert.Equal(
            "Query: \"buckaroo\" · 128 results · Page 2",
            SearchDisplayText.QuerySummaryText("buckaroo", 128, 2));
    }
}