using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// A page of Internet Archive search results together with the total number of
/// matching documents reported by the server.
/// </summary>
public sealed class InternetArchiveSearchPage
{
    /// <summary>Total number of matching documents (response.numFound).</summary>
    public long NumFound { get; }

    /// <summary>The results parsed for this page (identifier-filtered).</summary>
    public IReadOnlyList<InternetArchiveSearchResult> Results { get; }

    public InternetArchiveSearchPage(long numFound, IReadOnlyList<InternetArchiveSearchResult> results)
    {
        NumFound = numFound;
        Results = results ?? new List<InternetArchiveSearchResult>();
    }
}