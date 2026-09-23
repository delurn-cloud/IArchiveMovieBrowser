namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Immutable, validated search criteria for an Internet Archive search: an optional title
/// phrase, an optional actor/creator phrase, an optional inclusive year range, and the
/// media-type <see cref="SearchScope"/>. At least one of title / actor-creator / year-from /
/// year-to is required before a live search is issued. Instances are produced by the centralized
/// validation/query logic and carried unchanged through the launcher, completed-search state, and
/// paging/Refresh.
/// </summary>
public sealed class SearchCriteria
{
    /// <summary>Trimmed literal title phrase, or null when absent.</summary>
    public string? Title { get; }

    /// <summary>Trimmed literal actor/creator phrase, or null when absent.</summary>
    public string? Creator { get; }

    /// <summary>Validated inclusive lower year bound, or null when absent.</summary>
    public int? YearFrom { get; }

    /// <summary>Validated inclusive upper year bound, or null when absent.</summary>
    public int? YearTo { get; }

    /// <summary>
    /// The validated maximum accepted year (current year + 1) at criteria-construction time.
    /// Used to complete a from-only range query. Null when no year endpoint is present.
    /// </summary>
    public int? YearMax { get; }

    /// <summary>The media-type restriction to apply.</summary>
    public SearchScope Scope { get; }

    /// <summary>
    /// The selected genre keys (in declared catalog order, deduplicated) that constrain results to
    /// items matching any of those IA <c>subject</c> phrases. Empty when no genre filter is active.
    /// Genres are an optional narrowing filter and never make an otherwise-blank search valid.
    /// </summary>
    public IReadOnlyList<string> SelectedGenres { get; }

    /// <summary>
    /// True when at least one of title / actor-creator / year-from / year-to is present, i.e. a
    /// search can run.
    /// </summary>
    public bool HasAnyCriterion { get; }

    /// <summary>
    /// True when both year endpoints are present and equal: the historical exact-year case.
    /// </summary>
    public bool IsExactYear =>
        YearFrom is not null && YearTo is not null && YearFrom == YearTo;

    private SearchCriteria(
        string? title,
        string? creator,
        int? yearFrom,
        int? yearTo,
        int? yearMax,
        SearchScope scope,
        IReadOnlyList<string> selectedGenres,
        bool hasAnyCriterion)
    {
        Title = title;
        Creator = creator;
        YearFrom = yearFrom;
        YearTo = yearTo;
        YearMax = yearMax;
        Scope = scope;
        SelectedGenres = selectedGenres ?? new System.Collections.Generic.List<string>();
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
            null,
            null,
            scope,
            new System.Collections.Generic.List<string>(),
            hasAny);
    }

    /// <summary>
    /// Builds a criteria from already-validated fields (including the current-year-based maximum
    /// bound). The caller is responsible for deciding validity; this normalizes whitespace and
    /// recomputes <see cref="HasAnyCriterion"/>.
    /// </summary>
    public static SearchCriteria Validated(
        string? title,
        string? creator,
        int? yearFrom,
        int? yearTo,
        int currentYear,
        SearchScope scope)
    {
        string trimmedTitle = string.IsNullOrWhiteSpace(title) ? "" : title!.Trim();
        string trimmedCreator = string.IsNullOrWhiteSpace(creator) ? "" : creator!.Trim();
        bool hasYear = yearFrom is not null || yearTo is not null;
        bool hasAny = !string.IsNullOrWhiteSpace(trimmedTitle)
            || !string.IsNullOrWhiteSpace(trimmedCreator)
            || hasYear;

        int? yearMax = hasYear ? currentYear + 1 : null;
        return new SearchCriteria(
            string.IsNullOrWhiteSpace(trimmedTitle) ? null : trimmedTitle,
            string.IsNullOrWhiteSpace(trimmedCreator) ? null : trimmedCreator,
            yearFrom,
            yearTo,
            yearMax,
            scope,
            new System.Collections.Generic.List<string>(),
            hasAny);
    }

    /// <summary>
    /// Returns a new criteria identical to this one except that the selected genre keys are replaced
    /// by the given list, normalized to the declared catalog order and deduplicated. All other
    /// criteria (title, actor-creator, year endpoints, scope, validity) are preserved unchanged.
    /// Clearing genres (passing an empty list) therefore leaves every other filter intact.
    /// </summary>
    public SearchCriteria WithGenres(System.Collections.Generic.IReadOnlyList<string> genreKeys)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            GenreCatalog.NormalizeSelection(genreKeys),
            HasAnyCriterion);
    }
}