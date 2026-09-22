using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Builds the Internet Archive advanced-search query expression for structured search criteria
/// under a chosen <see cref="SearchScope"/>.
///
/// Title text is treated as one literal phrase: trimmed, escaped so it cannot act as Lucene/IA
/// query syntax, and wrapped as a quoted field phrase. Actor/creator text is treated as safe
/// tokenized keyword matching: the trimmed input is split into whitespace-delimited tokens, each
/// token is emitted as a literal unquoted field token (or a safely quoted literal where it could
/// otherwise carry Lucene meaning), and tokens are combined with an app-controlled uppercase
/// <c>AND</c> inside a field group so a remembered name part stages last-name searches without
/// any wildcard syntax. The media type restriction the scope implies is appended as before.
/// The builder is pure and WPF-independent so the mapping can be unit-tested without UI automation.
/// </summary>
public static class SearchScopeQueryBuilder
{
    /// <summary>
    /// Legacy title-only entry point retained for compatibility. Equivalent to building a title-only
    /// <see cref="SearchCriteria"/> and calling <see cref="BuildQuery(SearchCriteria)"/>.
    /// </summary>
    public static string BuildQuery(string? userTerms, SearchScope scope)
    {
        return BuildQuery(SearchCriteria.TitleOnly(userTerms, scope));
    }

    /// <summary>
    /// Returns the advanced-search <c>q</c> expression for the given criteria. Clauses are
    /// emitted deterministically as: <c>title:</c> phrase, <c>creator:</c> tokenized keyword
    /// clause, <c>year:</c> value, then the scope condition, each joined with an uppercase
    /// <c>AND</c>.
    /// </summary>
    public static string BuildQuery(SearchCriteria criteria)
    {
        return criteria.Scope switch
        {
            SearchScope.WatchableVideo => BuildClauseList(criteria) + " AND mediatype:movies",
            SearchScope.RelatedMaterials => BuildClauseList(criteria) + " AND NOT mediatype:movies",
            _ => BuildClauseList(criteria)
        };
    }

    /// <summary>Joins the title / creator / year clauses with " AND " in fixed order.</summary>
    private static string BuildClauseList(SearchCriteria criteria)
    {
        string parts = "";
        bool first = true;

        if (!string.IsNullOrWhiteSpace(criteria.Title))
        {
            parts = parts + "title:\"" + EscapeLiteral(criteria.Title) + "\"";
            first = false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.Creator))
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + BuildCreatorClause(criteria.Creator);
            first = false;
        }

        if (criteria.Year is not null)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + "year:" + criteria.Year.ToString();
            first = false;
        }

        return parts;
    }

    /// <summary>
    /// Builds the <c>creator:</c> clause for the trimmed, whitespace-delimited creator text.
    /// Each token is emitted as a literal field token (or a safely quoted literal where needed)
    /// and tokens are combined with an app-controlled uppercase <c>AND</c> inside a field group.
    /// </summary>
    private static string BuildCreatorClause(string? creatorText)
    {
        string trimmed = string.IsNullOrWhiteSpace(creatorText) ? "" : creatorText.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        var tokens = new System.Collections.Generic.List<string>();
        string current = "";
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (IsWhitespace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current);
                    current = "";
                }
            }
            else
            {
                current = current + c;
            }
        }
        if (current.Length > 0)
        {
            tokens.Add(current);
        }

        string combined = "";
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                combined = combined + " AND ";
            }
            combined = combined + CreatorToken(tokens[i]);
        }

        if (tokens.Count == 1)
        {
            return "creator:" + combined;
        }
        return "creator:(" + combined + ")";
    }

    /// <summary>
    /// Turns one whitespace-delimited token into a literal creator field value. A token that is a
    /// Boolean word (<c>AND</c>/<c>OR</c>/<c>NOT</c>) or that contains any Lucene-sensitive
    /// character is emitted as a safely escaped quoted phrase so it can never act as an operator,
    /// field, group, wildcard, range, or modifier. Otherwise it is emitted as a plain unquoted
    /// field token (the only form that allows last-name keyword matching without wildcards).
    /// </summary>
    private static string CreatorToken(string token)
    {
        if (IsBooleanWord(token) || !IsSafeToken(token))
        {
            return "\"" + EscapeLiteral(token) + "\"";
        }
        return token;
    }

    private static bool IsBooleanWord(string token)
    {
        return string.Equals(token, "AND", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "OR", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NOT", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a token is safe to emit unquoted as a field term: no character that Lucene
    /// interprets as an operator, field separator, wildcard, grouping, range, or modifier.
    /// </summary>
    private static bool IsSafeToken(string token)
    {
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (c == '\\' || c == '"' || c == '*' || c == '?' || c == '+' || c == '-' || c == '!'
                || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}'
                || c == '^' || c == '~' || c == ':' || c == '/' || c == '&' || c == '|')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
    }
    /// <summary>
    /// Trims surrounding whitespace and neutralizes the only characters that retain meaning
    /// inside a Lucene quoted phrase (backslash and double-quote) so arbitrary user text is
    /// always treated as a literal field phrase rather than query operators. Colon, plus/minus,
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
