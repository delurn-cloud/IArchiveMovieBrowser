using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// An immutable snapshot of a completed Internet Archive search that the MainWindow
/// handshakes to the separate Search Results window. It carries only the values the
/// results view needs to render a page: the query text, the reported total match count,
/// the page number, and the parsed results for that page.
/// </summary>
public sealed class CompletedSearch
{
    /// <summary>The user-entered query text (nonblank when this object is produced).</summary>
    public string Query { get; }

    /// <summary>Total number of matching items reported by the server (response.numFound).</summary>
    public long NumFound { get; }

    /// <summary>The one-based page number these results represent.</summary>
    public int Page { get; }

    /// <summary>The parsed results for this page (identifier-filtered already).</summary>
    public IReadOnlyList<InternetArchiveSearchResult> Results { get; }

    public CompletedSearch(
        string query,
        long numFound,
        int page,
        IReadOnlyList<InternetArchiveSearchResult> results)
    {
        Query = query;
        NumFound = numFound;
        Page = page;
        Results = results ?? new List<InternetArchiveSearchResult>();
    }
}