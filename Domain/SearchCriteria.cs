namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Immutable, validated search criteria for an Internet Archive search: an optional title
/// phrase, an optional actor/creator phrase, an optional exact year, and the media-type
/// <see cref="SearchScope"/>. At least one of title / actor-creator / year is required before a
/// live search is issued. Instances are produced by the centralized validation/query logic and
/// carried unchanged through the launcher, completed-search state, and paging/Refresh.
/// </summary>
public sealed class SearchCriteria
{
    /// <summary>Trimmed literal title phrase, or null when absent.</summary>
    public string? Title { get; }

    /// <summary>Trimmed literal actor/creator phrase, or null when absent.</summary>
    public string? Creator { get; }

    /// <summary>Validated exact year filter, or null when absent.</summary>
    public int? Year { get; }

    /// <summary>The media-type restriction to apply.</summary>
    public SearchScope Scope { get; }

    /// <summary>
    /// True when at least one of title / actor-creator / year is present, i.e. a search can run.
    /// </summary>
    public bool HasAnyCriterion { get; }

    private SearchCriteria(
        string? title,
        string? creator,
        int? year,
        SearchScope scope,
        bool hasAnyCriterion)
    {
        Title = title;
        Creator = creator;
        Year = year;
        Scope = scope;
        HasAnyCriterion = hasAnyCriterion;
    }

    /// <summary>
    /// Builds a criteria with only a (trimmed) title and scope; used by the legacy request and
    /// completed-search constructors so title-only behavior is unchanged.
    /// </summary>
    public static SearchCriteria TitleOnly(string? title, SearchScope scope)
    {
        string trimmed = string.IsNullOrWhiteSpace(title) ? "" : title!.Trim();
        bool hasAny = !string.IsNullOrWhiteSpace(trimmed);
        return new SearchCriteria(
            hasAny ? trimmed : null,
            null,
            null,
            scope,
            hasAny);
    }

    /// <summary>
    /// Builds a criteria from already-validated fields. The caller is responsible for deciding
    /// validity; this normalizes whitespace and recomputes <see cref="HasAnyCriterion"/>.
    /// </summary>
    public static SearchCriteria Validated(
        string? title,
        string? creator,
        int? year,
        SearchScope scope)
    {
        string trimmedTitle = string.IsNullOrWhiteSpace(title) ? "" : title!.Trim();
        string trimmedCreator = string.IsNullOrWhiteSpace(creator) ? "" : creator!.Trim();
        bool hasAny = !string.IsNullOrWhiteSpace(trimmedTitle)
            || !string.IsNullOrWhiteSpace(trimmedCreator)
            || year is not null;

        return new SearchCriteria(
            string.IsNullOrWhiteSpace(trimmedTitle) ? null : trimmedTitle,
            string.IsNullOrWhiteSpace(trimmedCreator) ? null : trimmedCreator,
            year,
            scope,
            hasAny);
    }
}