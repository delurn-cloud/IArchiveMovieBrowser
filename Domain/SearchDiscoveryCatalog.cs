using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// The single source of truth for discovery-search query text: which Internet Archive fields keywords
/// target, the fixed television collection set, and the ordering parameters.
///
/// Every collection name and sort specification originates here and is app-controlled literal text,
/// never arbitrary user input, mirroring how <see cref="GenreCatalog"/> is the only origin of genre
/// subject phrases. The catalog is pure and WPF-independent so it can be unit-tested without any UI
/// dependency, and the emitted clauses are never assembled from UI label text.
/// </summary>
public static class SearchDiscoveryCatalog
{
    /// <summary>
    /// The fixed set of best-known Internet Archive television collections, used consistently by both
    /// the include and exclude variants of <see cref="VideoCollectionScope"/>.
    /// </summary>
    public static IReadOnlyList<string> TelevisionCollections()
    {
        return new List<string>
        {
            "television",
            "classic_tv",
            "television_inbox"
        };
    }

    /// <summary>
    /// The Internet Archive metadata field a keyword term is matched against when
    /// <paramref name="fields"/> is <see cref="SearchKeywordFields.Subjects"/>.
    /// </summary>
    public const string SubjectField = "subject";

    /// <summary>
    /// The Internet Archive metadata field a keyword term is additionally matched against when
    /// <paramref name="fields"/> is <see cref="SearchKeywordFields.SubjectsAndDescriptions"/>.
    /// </summary>
    public const string DescriptionField = "description";

    /// <summary>
    /// The Internet Archive metadata field the television-collection narrowing targets. Kept as
    /// app-controlled literal text so collection clauses are never assembled from UI wording.
    /// </summary>
    public const string CollectionField = "collection";

    /// <summary>
    /// The metadata fields a term targets, in the fixed order they are OR-ed. Exactly one field for
    /// <see cref="SearchKeywordFields.Subjects"/>; two for
    /// <see cref="SearchKeywordFields.SubjectsAndDescriptions"/>.
    /// </summary>
    public static IReadOnlyList<string> KeywordFieldsFor(SearchKeywordFields fields)
    {
        if (fields == SearchKeywordFields.SubjectsAndDescriptions)
        {
            return new List<string> { SubjectField, DescriptionField };
        }

        return new List<string> { SubjectField };
    }

    /// <summary>
    /// True when the given field-selection option targets more than one metadata field, which is the
    /// condition under which a per-term OR group (and therefore parentheses) must be emitted.
    /// </summary>
    public static bool RequiresPerTermOrGroup(SearchKeywordFields fields)
    {
        return KeywordFieldsFor(fields).Count > 1;
    }

    /// <summary>
    /// The <c>sort[]</c> request parameters for an ordering, in the exact order they must be appended.
    /// Empty for <see cref="SearchOrdering.InternetArchiveDefault"/>, so the default sends no sort
    /// parameter at all and the Internet Archive's own ordering applies.
    /// </summary>
    public static IReadOnlyList<string> SortParametersFor(SearchOrdering ordering)
    {
        if (ordering == SearchOrdering.MostDownloaded)
        {
            // Downloads descending, then ascending identifier so equal download counts resolve
            // deterministically within a single ordering. This is not a snapshot guarantee.
            return new List<string>
            {
                "sort[]=downloads desc",
                "sort[]=identifier asc"
            };
        }

        return new List<string>();
    }

    /// <summary>True when the ordering sends at least one <c>sort[]</c> parameter.</summary>
    public static bool HasSortParameters(SearchOrdering ordering)
    {
        return SortParametersFor(ordering).Count > 0;
    }
}