namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// A validated request to search the Internet Archive catalog.
/// Holds the structured search criteria (title / actor-creator / year + scope) and pagination
/// bounds. Legacy string-based constructors are retained: they build a title-only
/// <see cref="SearchCriteria"/> and behave exactly as before.
/// </summary>
public sealed class InternetArchiveSearchRequest
{
    /// <summary>User-entered title search text (percent-encoded when sent), or "" for actor/year-only.</summary>
    public string Query { get; }

    /// <summary>The full structured criteria this request searches with.</summary>
    public SearchCriteria Criteria { get; }

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
        ValidatePaging(page, pageSize);

        Criteria = SearchCriteria.TitleOnly(query, scope);
        Query = query!;
        Page = page;
        PageSize = pageSize;
        Scope = scope;
    }

    /// <summary>Builds a request from full structured criteria; at least one criterion is required.</summary>
    public InternetArchiveSearchRequest(
        SearchCriteria criteria,
        int page = 1,
        int pageSize = 25)
    {
        if (criteria is null)
        {
            throw new ArgumentNullException(nameof(criteria));
        }
        if (!criteria.HasAnyCriterion)
        {
            throw new ArgumentException(
                "At least one search criterion (title, actor/creator, or year) is required.",
                nameof(criteria));
        }
        ValidatePaging(page, pageSize);

        Criteria = criteria;
        Query = criteria.Title ?? "";
        Scope = criteria.Scope;
        Page = page;
        PageSize = pageSize;
    }

    private static void ValidatePaging(int page, int pageSize)
    {
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
    }
}