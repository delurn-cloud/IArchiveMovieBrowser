namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Immutable, validated search criteria for an Internet Archive search: an optional title
/// phrase, an optional actor/creator phrase, an optional inclusive year range, the
/// media-type <see cref="SearchScope"/>, and the discovery dimensions (keywords, keyword fields,
/// match mode, excluded terms, video collections, ordering). At least one of title /
/// actor-creator / year-from / year-to / keywords / excluded terms is required before a live search
/// is issued. Instances are produced by the centralized validation/query logic and carried unchanged
/// through the launcher, completed-search state, and paging/Refresh.
///
/// <para>Every dimension here rides on the same immutable instance, which is what lets paging and
/// refresh re-issue an identical query and identical ordering without re-reading any control.</para>
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
    /// True when trailer, teaser, and promotional entries should be omitted from results. This is an
    /// aggressive, opt-in narrowing filter: it excludes items by Internet Archive collection/subject
    /// metadata and by the presence of the word "trailer" in the title, so it can also hide full
    /// movies, mixed "with trailers" uploads, and titles that merely contain the word (for example
    /// <c>Trailer Park Boys</c>). It never makes an otherwise-blank search valid, and it does not
    /// promise to remove every teaser, TV spot, or game promo: those disappear only when they match
    /// the metadata/title rules. False by default.
    /// </summary>
    public bool ExcludeTrailers { get; }

    /// <summary>
    /// The literal positive keyword terms or phrases, already parsed and validated by
    /// <see cref="SearchKeywordPhraseParser"/>. Empty when no keyword filter is active. Keywords are
    /// literal-only: there is no synonym expansion and no automatic broadening. Unlike genres and
    /// trailer exclusion, keywords are a primary dimension — on their own they make a search valid.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Which metadata fields each keyword/excluded term is matched against.</summary>
    public SearchKeywordFields KeywordFields { get; }

    /// <summary>How the individual keyword terms are combined into one clause.</summary>
    public SearchKeywordMatchMode MatchMode { get; }

    /// <summary>
    /// The literal terms or phrases to exclude, already parsed and validated by
    /// <see cref="SearchKeywordPhraseParser"/>. Empty when nothing is excluded. Excluded terms are
    /// always combined with <c>AND</c> and are independent of <see cref="MatchMode"/>.
    /// </summary>
    public IReadOnlyList<string> ExcludedTerms { get; }

    /// <summary>
    /// The optional television-collection narrowing. This is a separate type from the primary
    /// <see cref="Scope"/> because the two are orthogonal: this one only adds <c>collection:</c>
    /// clauses, while <see cref="Scope"/> controls media type.
    /// </summary>
    public VideoCollectionScope VideoCollections { get; }

    /// <summary>Result ordering carried with the criteria so paging and refresh preserve it.</summary>
    public SearchOrdering Ordering { get; }

    /// <summary>
    /// True when at least one primary criterion is present — title, actor-creator, a year endpoint,
    /// keywords, or excluded terms — i.e. a search can run.
    ///
    /// <para>Genres and <see cref="ExcludeTrailers"/> are deliberately <b>not</b> included: they are
    /// secondary narrowing options that cannot start a search on their own.</para>
    /// </summary>
    public bool HasAnyCriterion =>
        !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Creator)
        || YearFrom is not null
        || YearTo is not null
        || (Keywords is not null && Keywords.Count > 0)
        || (ExcludedTerms is not null && ExcludedTerms.Count > 0);

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
        bool excludeTrailers,
        IReadOnlyList<string> keywords,
        SearchKeywordFields keywordFields,
        SearchKeywordMatchMode matchMode,
        IReadOnlyList<string> excludedTerms,
        VideoCollectionScope videoCollections,
        SearchOrdering ordering)
    {
        Title = title;
        Creator = creator;
        YearFrom = yearFrom;
        YearTo = yearTo;
        YearMax = yearMax;
        Scope = scope;
        SelectedGenres = selectedGenres ?? new System.Collections.Generic.List<string>();
        ExcludeTrailers = excludeTrailers;
        Keywords = keywords ?? new System.Collections.Generic.List<string>();
        KeywordFields = keywordFields;
        MatchMode = matchMode;
        ExcludedTerms = excludedTerms ?? new System.Collections.Generic.List<string>();
        VideoCollections = videoCollections;
        Ordering = ordering;
    }

    /// <summary>
    /// Builds a criteria with only a (trimmed) title and scope; used by the legacy request and
    /// completed-search constructors so title-only behavior is unchanged.
    /// </summary>
    public static SearchCriteria TitleOnly(string? title, SearchScope scope)
    {
        string trimmed = string.IsNullOrWhiteSpace(title) ? "" : title!.Trim();
        return new SearchCriteria(
            string.IsNullOrWhiteSpace(trimmed) ? null : trimmed,
            null,
            null,
            null,
            null,
            scope,
            new System.Collections.Generic.List<string>(),
            false,
            new System.Collections.Generic.List<string>(),
            SearchKeywordFields.Subjects,
            SearchKeywordMatchMode.AllTerms,
            new System.Collections.Generic.List<string>(),
            VideoCollectionScope.AllWatchableVideo,
            SearchOrdering.InternetArchiveDefault);
    }

    /// <summary>
    /// Builds a criteria from already-validated fields (including the current-year-based maximum
    /// bound), with trailer exclusion off. The caller is responsible for deciding validity; this
    /// normalizes whitespace and recomputes <see cref="HasAnyCriterion"/>.
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

        int? yearMax = hasYear ? currentYear + 1 : null;
        return new SearchCriteria(
            string.IsNullOrWhiteSpace(trimmedTitle) ? null : trimmedTitle,
            string.IsNullOrWhiteSpace(trimmedCreator) ? null : trimmedCreator,
            yearFrom,
            yearTo,
            yearMax,
            scope,
            new System.Collections.Generic.List<string>(),
            false,
            new System.Collections.Generic.List<string>(),
            SearchKeywordFields.Subjects,
            SearchKeywordMatchMode.AllTerms,
            new System.Collections.Generic.List<string>(),
            VideoCollectionScope.AllWatchableVideo,
            SearchOrdering.InternetArchiveDefault);
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
            ExcludeTrailers,
            Keywords,
            KeywordFields,
            MatchMode,
            ExcludedTerms,
            VideoCollections,
            Ordering);
    }

    /// <summary>
    /// Returns a new criteria identical to this one except that trailer exclusion is set to
    /// <paramref name="excludeTrailers"/>. All other criteria (title, actor-creator, year endpoints,
    /// scope, selected genres, validity) are preserved unchanged, so this composes in either order
    /// with <see cref="WithGenres"/>. Because <see cref="HasAnyCriterion"/> is carried through
    /// untouched, turning exclusion on or off never makes an otherwise-blank search valid.
    /// </summary>
    public SearchCriteria WithExcludeTrailers(bool excludeTrailers)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            SelectedGenres,
            excludeTrailers,
            Keywords,
            KeywordFields,
            MatchMode,
            ExcludedTerms,
            VideoCollections,
            Ordering);
    }

    /// <summary>
    /// Returns a new criteria identical to this one except that the parsed positive keyword terms and
    /// their field/mode selection are replaced. All other criteria are preserved unchanged, so this
    /// composes in any order with the other combinators. Because <see cref="HasAnyCriterion"/> is
    /// computed over all primary dimensions, keywords alone make an otherwise-blank search valid.
    /// </summary>
    public SearchCriteria WithKeywords(
        System.Collections.Generic.IReadOnlyList<string> keywords,
        SearchKeywordFields keywordFields,
        SearchKeywordMatchMode matchMode)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            SelectedGenres,
            ExcludeTrailers,
            NormalizeTerms(keywords),
            keywordFields,
            matchMode,
            ExcludedTerms,
            VideoCollections,
            Ordering);
    }

    /// <summary>
    /// Returns a new criteria identical to this one except that the parsed excluded terms are
    /// replaced. Excluded terms alone make an otherwise-blank search valid. The keyword field
    /// selection applies to exclusions too, so this combinator does not take its own field argument.
    /// </summary>
    public SearchCriteria WithExcludedTerms(System.Collections.Generic.IReadOnlyList<string> excludedTerms)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            SelectedGenres,
            ExcludeTrailers,
            Keywords,
            KeywordFields,
            MatchMode,
            NormalizeTerms(excludedTerms),
            VideoCollections,
            Ordering);
    }

    /// <summary>Returns a new criteria identical to this one except for the television-collection narrowing.</summary>
    public SearchCriteria WithVideoCollections(VideoCollectionScope videoCollections)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            SelectedGenres,
            ExcludeTrailers,
            Keywords,
            KeywordFields,
            MatchMode,
            ExcludedTerms,
            videoCollections,
            Ordering);
    }

    /// <summary>
    /// Returns a new criteria identical to this one except for the result ordering. Carrying the
    /// ordering here is what lets paging and refresh re-issue the identical sort parameters.
    /// </summary>
    public SearchCriteria WithOrdering(SearchOrdering ordering)
    {
        return new SearchCriteria(
            Title,
            Creator,
            YearFrom,
            YearTo,
            YearMax,
            Scope,
            SelectedGenres,
            ExcludeTrailers,
            Keywords,
            KeywordFields,
            MatchMode,
            ExcludedTerms,
            VideoCollections,
            ordering);
    }

    /// <summary>
    /// Copies a term list, dropping null/blank entries so a criteria instance never holds an empty
    /// term that could emit an empty quoted literal.
    /// </summary>
    private static System.Collections.Generic.IReadOnlyList<string> NormalizeTerms(
        System.Collections.Generic.IReadOnlyList<string>? terms)
    {
        var kept = new System.Collections.Generic.List<string>();
        if (terms is null)
        {
            return kept;
        }

        foreach (string term in terms)
        {
            if (!string.IsNullOrWhiteSpace(term))
            {
                kept.Add(term);
            }
        }

        return kept;
    }
}