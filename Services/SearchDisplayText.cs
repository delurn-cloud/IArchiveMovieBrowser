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

    /// <summary>Previous is enabled on any page after the first.</summary>
    public static bool PreviousEnabled(int page) => page > 1;

    /// <summary>Next is enabled while more pages exist after the current one.</summary>
    public static bool NextEnabled(long numFound, int page, int pageSize)
    {
        return numFound > (long)page * (long)pageSize;
    }
}