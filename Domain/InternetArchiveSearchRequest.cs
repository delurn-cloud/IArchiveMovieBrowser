namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// A validated request to search the Internet Archive catalog.
/// Holds the user-entered search text, the search scope (media-type restriction), and
/// pagination bounds.
/// </summary>
public sealed class InternetArchiveSearchRequest
{
    /// <summary>User-entered title search text (percent-encoded when sent).</summary>
    public string Query { get; }

    /// <summary>One-based page number; minimum 1.</summary>
    public int Page { get; }

    /// <summary>Number of rows per page; between 1 and 100 inclusive.</summary>
    public int PageSize { get; }

    /// <summary>
    /// Media-type restriction applied to the search. Defaults to
    /// <see cref="SearchScope.Everything"/> (no restriction).
    /// </summary>
    public SearchScope Scope { get; }

    public InternetArchiveSearchRequest(string? query, int page = 1, int pageSize = 25)
        : this(query, page, pageSize, SearchScope.Everything)
    {
    }

    public InternetArchiveSearchRequest(
        string? query,
        int page,
        int pageSize,
        SearchScope scope)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException(
                "Search query must not be null, empty, or whitespace-only.",
                nameof(query));
        }
        if (page < 1)
        {
            throw new ArgumentException("Search page must be at least 1.", nameof(page));
        }
        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentException(
                "Search page size must be between 1 and 100.",
                nameof(pageSize));
        }

        Query = query!;
        Page = page;
        PageSize = pageSize;
        Scope = scope;
    }
}