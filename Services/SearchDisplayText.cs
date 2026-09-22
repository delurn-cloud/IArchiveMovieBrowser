using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Pure, WPF-independent text and state helpers for the search-results screen.
/// None of these methods touch UI, networking, or JSON; they exist so the display logic
/// can be unit-tested without UI automation.
/// </summary>
public static class SearchDisplayText
{
    public const string UntitledText = "(untitled)";
    public const string UnknownCreatorText = "(unknown creator)";
    public const string UnknownMediaTypeText = "(unknown type)";
    public const string UnknownDateText = "(no date)";
    public const string NoDetailsText = "(no additional details)";
    private const string Separator = " · ";

    /// <summary>Line 1 of a result row: the title, or a human-readable fallback.</summary>
    public static string TitleText(InternetArchiveSearchResult result)
    {
        return string.IsNullOrWhiteSpace(result.Title) ? UntitledText : result.Title;
    }

    /// <summary>Date/year segment; prefers Date, then Year, then a fallback.</summary>
    public static string DateYearText(InternetArchiveSearchResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Date))
        {
            return result.Date;
        }
        if (!string.IsNullOrWhiteSpace(result.Year))
        {
            return result.Year;
        }
        return UnknownDateText;
    }

    /// <summary>Creators joined with ", ", or a fallback when absent.</summary>
    public static string CreatorsText(InternetArchiveSearchResult result)
    {
        if (result.Creators is null || result.Creators.Count == 0)
        {
            return UnknownCreatorText;
        }

        string text = "";
        bool first = true;
        foreach (string creator in result.Creators)
        {
            if (string.IsNullOrWhiteSpace(creator))
            {
                continue;
            }
            if (!first)
            {
                text = text + ", ";
            }
            text = text + creator;
            first = false;
        }
        return text.Length == 0 ? UnknownCreatorText : text;
    }

    /// <summary>Media-type segment, or a fallback when absent.</summary>
    public static string MediaTypeText(InternetArchiveSearchResult result)
    {
        return string.IsNullOrWhiteSpace(result.MediaType) ? UnknownMediaTypeText : result.MediaType;
    }

    /// <summary>
    /// Line 2 of a result row: date/year, creator(s), and media type joined without empty
    /// separators (each segment always carries a fallback when its field is missing).
    /// </summary>
    public static string SecondLineText(InternetArchiveSearchResult result)
    {
        var parts = new List<string>
        {
            DateYearText(result),
            CreatorsText(result),
            MediaTypeText(result)
        };

        string text = "";
        bool first = true;
        foreach (string part in parts)
        {
            if (!first)
            {
                text = text + Separator;
            }
            text = text + part;
            first = false;
        }
        return text;
    }

    /// <summary>Complete two-line text for one result ListView row.</summary>
    public static string RowText(InternetArchiveSearchResult result)
    {
        return TitleText(result) + "\n" + SecondLineText(result);
    }

    /// <summary>Result-count and current-page summary, e.g. "128 results · Page 2".</summary>
    public static string ResultsSummary(long numFound, int page)
    {
        return numFound.ToString() + " results" + Separator + "Page " + page.ToString();
    }

    public static string StatusLoading() => "Loading…";
    public static string StatusReady(int page) => "Ready — showing page " + page.ToString() + ".";
    public static string StatusNoResults() => "No results.";
    public static string StatusCancelled() => "Cancelled.";
    public static string StatusError(string message) => "Error: " + message;
    public static string EmptyQueryText() => "Enter a search term.";

    // --- Compact launcher (MainWindow) helpers --------------------------------------

    public const string LauncherTitleText = "Search Internet Archive";
    public const string LauncherInstructionText =
        "Search public Internet Archive movie and video titles.";

    public static string SearchingText() => "Searching Internet Archive…";

    public static string FoundText(long numFound) =>
        "Search complete — " + numFound.ToString() + " matching items found.";

    public static string NoMatchingItemsText() => "No matching Internet Archive items found.";

    public static string OpenResultsText(long numFound) =>
        "Open Results (" + numFound.ToString() + ")";

    public const string NoResultsLabelText = "No Results";

    /// <summary>Visible label for the optional creator narrow filter (user-facing wording).</summary>
    public const string MadeByCreditedToLabel = "Made by / credited to";

    /// <summary>Accessible help text for the creator narrow filter (user-facing wording).</summary>
    public const string MadeByCreditedToHelp =
        "Optionally narrow results by a name or organization in Internet Archive creator metadata, " +
        "such as a director, producer, studio, uploader, or curator. This does not search actors or cast.";

    /// <summary>The Recent summary line used by the Search Results window.</summary>
    public static string QuerySummaryText(string query, long numFound, int page) =>
        "Query: \"" + query + "\"" + Separator + ResultsSummary(numFound, page);

    /// <summary>
    /// Summary line for a completed search built from structured criteria. For a title search it
    /// matches the legacy format exactly; for actor-only/year-only searches it shows a concise
    /// description of the applied criteria instead of an empty title label.
    /// </summary>
    public static string QuerySummaryText(SearchCriteria criteria, long numFound, int page)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Title))
        {
            return QuerySummaryText(criteria.Title!, numFound, page);
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(criteria.Creator))
        {
            parts.Add("creator \"" + criteria.Creator!.Trim() + "\"");
        }
        if (criteria.Year is not null)
        {
            parts.Add("year " + criteria.Year.ToString());
        }

        string label = parts.Count == 0 ? "criteria" : string.Join(", ", parts);
        return "Query: " + label + Separator + ResultsSummary(numFound, page);
    }

    // --- Search scope selection (launcher) ----------------------------------------

    public const string ScopeLabelText = "Search scope";
    public const string WatchableVideoScopeLabel = "Watchable video";
    public const string RelatedMaterialsScopeLabel = "Related materials";
    public const string EverythingScopeLabel = "Everything";

    public const string RelatedMaterialsTooltip =
        "Non-video Internet Archive items matching your search, such as texts, audio, software, or images.";

    /// <summary>Human-readable label for a scope, matching the launcher ComboBox items.</summary>
    public static string ScopeLabel(SearchScope scope)
    {
        return scope switch
        {
            SearchScope.WatchableVideo => WatchableVideoScopeLabel,
            SearchScope.RelatedMaterials => RelatedMaterialsScopeLabel,
            _ => EverythingScopeLabel
        };
    }

    /// <summary>
    /// The ordered scope options as shown in the launcher, with Watchable video first so it
    /// is the default selection.
    /// </summary>
    public static IReadOnlyList<SearchScope> ScopeOptions()
    {
        return new List<SearchScope>
        {
            SearchScope.WatchableVideo,
            SearchScope.RelatedMaterials,
            SearchScope.Everything
        };
    }

    /// <summary>Previous is enabled on any page after the first.</summary>
    public static bool PreviousEnabled(int page) => page > 1;

    /// <summary>Next is enabled while more pages exist after the current one.</summary>
    public static bool NextEnabled(long numFound, int page, int pageSize)
    {
        return numFound > (long)page * (long)pageSize;
    }
}