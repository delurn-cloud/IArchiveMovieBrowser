using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Builds the Internet Archive advanced-search query expression for a user's search terms
/// under a chosen <see cref="SearchScope"/>.
///
/// The user's normal text is treated as one literal title phrase: it is trimmed, escaped so it
/// cannot act as Lucene/IA query syntax, and wrapped as <c>title:"&lt;phrase&gt;"</c>. The media
/// type restriction the scope implies is appended as before. The builder is pure and
/// WPF-independent so the mapping can be unit-tested without UI automation.
/// </summary>
public static class SearchScopeQueryBuilder
{
    /// <summary>
    /// Returns the advanced-search <c>q</c> expression for the given user terms and scope.
    /// The user text is trimmed and wrapped as a quoted literal title phrase.
    /// </summary>
    public static string BuildQuery(string? userTerms, SearchScope scope)
    {
        string titleQuery = "title:\"" + EscapeLiteral(userTerms) + "\"";

        return scope switch
        {
            SearchScope.WatchableVideo => titleQuery + " AND mediatype:movies",
            SearchScope.RelatedMaterials => titleQuery + " AND NOT mediatype:movies",
            _ => titleQuery
        };
    }

    /// <summary>
    /// Trims surrounding whitespace and neutralizes the only characters that retain meaning
    /// inside a Lucene quoted phrase (backslash and double-quote) so arbitrary user text is
    /// always treated as a literal title phrase rather than query operators. Colon, plus/minus,
    /// parentheses, wildcards, and Boolean words carry no special meaning inside the quotes.
    /// The resulting literal is still percent-encoded by the client when building the request URI.
    /// </summary>
    private static string EscapeLiteral(string? userTerms)
    {
        if (string.IsNullOrEmpty(userTerms))
        {
            return "";
        }

        string trimmed = userTerms.Trim();

        string escaped = "";
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '\\')
            {
                escaped = escaped + "\\\\";
            }
            else if (c == '"')
            {
                escaped = escaped + "\\\"";
            }
            else
            {
                escaped = escaped + c;
            }
        }
        return escaped;
    }
}