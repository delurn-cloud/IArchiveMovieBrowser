using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Builds the Internet Archive advanced-search query expression for a user's search terms
/// under a chosen <see cref="SearchScope"/>.
///
/// The expression keeps the established title-search shape (<c>title:(&lt;terms&gt;)</c>) and
/// appends only the media-type restriction the scope implies. It is pure and WPF-independent
/// so the mapping can be unit-tested without UI automation.
/// </summary>
public static class SearchScopeQueryBuilder
{
    /// <summary>
    /// Returns the advanced-search <c>q</c> expression for the given user terms and scope.
    /// </summary>
    public static string BuildQuery(string userTerms, SearchScope scope)
    {
        string titleQuery = "title:(" + userTerms + ")";

        return scope switch
        {
            SearchScope.WatchableVideo => titleQuery + " AND mediatype:movies",
            SearchScope.RelatedMaterials => titleQuery + " AND NOT mediatype:movies",
            _ => titleQuery
        };
    }
}