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

    /// <summary>The structured criteria this search used and reuses on paging/Refresh.</summary>
    public SearchCriteria Criteria { get; }

    /// <summary>The media-type search scope used for this search.</summary>
    public SearchScope Scope { get; }

    public CompletedSearch(
        string query,
        long numFound,
        int page,
        IReadOnlyList<InternetArchiveSearchResult> results)
        : this(query, numFound, page, results, SearchScope.Everything)
    {
    }

    public CompletedSearch(
        string query,
        long numFound,
        int page,
        IReadOnlyList<InternetArchiveSearchResult> results,
        SearchScope scope)
    {
        Query = query;
        NumFound = numFound;
        Page = page;
        Results = results ?? new List<InternetArchiveSearchResult>();
        Scope = scope;
        Criteria = SearchCriteria.TitleOnly(query, scope);
    }

    public CompletedSearch(
        SearchCriteria criteria,
        long numFound,
        int page,
        IReadOnlyList<InternetArchiveSearchResult> results)
    {
        if (criteria is null)
        {
            throw new ArgumentNullException(nameof(criteria));
        }
        Criteria = criteria;
        Query = criteria.Title ?? "";
        NumFound = numFound;
        Page = page;
        Results = results ?? new List<InternetArchiveSearchResult>();
        Scope = criteria.Scope;
    }
}