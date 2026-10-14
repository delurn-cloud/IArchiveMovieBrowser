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
    /// Returns the advanced-search <c>q</c> expression for the given criteria. Clauses are emitted
    /// deterministically in this fixed order, each joined with an uppercase <c>AND</c>:
    /// <c>title:</c> phrase, keyword group, excluded-term clauses, <c>creator:</c> tokenized
    /// keyword clause, <c>year:</c> value, genre <c>OR</c> group, television-collection clause, the
    /// fixed trailer exclusion, then the scope condition.
    ///
    /// The order is fixed (not dependent on which control the user touched last) so the emitted
    /// query is deterministic and therefore assertable in tests.
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

    /// <summary>Joins every clause in the fixed documented order with " AND ".</summary>
    private static string BuildClauseList(SearchCriteria criteria)
    {
        string parts = "";
        bool first = true;

        if (!string.IsNullOrWhiteSpace(criteria.Title))
        {
            parts = parts + "title:\"" + EscapeLiteral(criteria.Title) + "\"";
            first = false;
        }

        string keywordClause = BuildKeywordClause(criteria.Keywords, criteria.KeywordFields, criteria.MatchMode);
        if (keywordClause.Length > 0)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + keywordClause;
            first = false;
        }

        string exclusionClause = BuildExclusionClause(criteria.ExcludedTerms, criteria.KeywordFields);
        if (exclusionClause.Length > 0)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + exclusionClause;
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

        if (criteria.YearFrom is not null || criteria.YearTo is not null)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + BuildYearClause(criteria);
            first = false;
        }

        if (criteria.SelectedGenres is not null && criteria.SelectedGenres.Count > 0)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + BuildGenreClause(criteria.SelectedGenres);
            first = false;
        }

        string collectionClause = BuildVideoCollectionClause(criteria.VideoCollections);
        if (collectionClause.Length > 0)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + collectionClause;
            first = false;
        }

        if (criteria.ExcludeTrailers)
        {
            if (!first)
            {
                parts = parts + " AND ";
            }
            parts = parts + TrailerExclusionClause;
            first = false;
        }

        return parts;
    }

    /// <summary>
    /// Builds the positive keyword clause from already-parsed literal terms.
    ///
    /// <para>Each term becomes a quoted literal field phrase. With
    /// <see cref="SearchKeywordFields.SubjectsAndDescriptions"/> the term is OR-ed across the
    /// selected fields and that inner OR is parenthesized, because it must stay a single term group
    /// before the terms are combined.</para>
    ///
    /// <para>Terms are combined with an app-controlled uppercase <c>AND</c> or <c>OR</c> in a
    /// parenthesized group. A single term emits no redundant outer parentheses — except that a
    /// multi-field term still carries the parentheses its inner OR requires.</para>
    ///
    /// <para><see cref="SearchKeywordMatchMode.ExactPhrase"/> needs no separate handling here: the
    /// parser has already collapsed the whole entry into a single term, which is exactly what this
    /// method emits. It is therefore joined with <c>AND</c> defensively, as a single-term group.</para>
    ///
    /// <para>There is no synonym expansion and no automatic broadening of any kind.</para>
    /// </summary>
    private static string BuildKeywordClause(
        IReadOnlyList<string> terms,
        SearchKeywordFields fields,
        SearchKeywordMatchMode mode)
    {
        if (terms is null || terms.Count == 0)
        {
            return "";
        }

        string op = mode == SearchKeywordMatchMode.AnyTerms ? " OR " : " AND ";

        string group = "";
        bool first = true;
        foreach (string term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue; // a blank term is never emitted as an empty literal
            }

            if (!first)
            {
                group = group + op;
            }
            group = group + BuildTermClause(term, fields);
            first = false;
        }

        if (group.Length == 0)
        {
            return "";
        }

        return terms.Count <= 1 ? group : "(" + group + ")";
    }

    /// <summary>
    /// Builds one term's clause: a quoted literal per targeted field, OR-ed and parenthesized when
    /// more than one field is selected.
    /// </summary>
    private static string BuildTermClause(string term, SearchKeywordFields fields)
    {
        string literal = EscapeLiteral(term);
        IReadOnlyList<string> fieldNames = SearchDiscoveryCatalog.KeywordFieldsFor(fields);

        string clause = "";
        bool first = true;
        foreach (string field in fieldNames)
        {
            if (!first)
            {
                clause = clause + " OR ";
            }
            clause = clause + field + ":\"" + literal + "\"";
            first = false;
        }

        return fieldNames.Count <= 1 ? clause : "(" + clause + ")";
    }

    /// <summary>
    /// Builds the excluded-term clauses. Every term is negated and joined with <c>AND</c>, so an item
    /// must match <b>none</b> of the excluded terms. A <c>NOT (a OR b)</c> group is deliberately
    /// never emitted: it would be a no-op that widens results rather than narrowing them.
    ///
    /// With a multi-field selection the negation wraps the per-field OR, because the item must be
    /// excluded when the term matches in <b>either</b> selected field.
    /// </summary>
    private static string BuildExclusionClause(
        IReadOnlyList<string> terms,
        SearchKeywordFields fields)
    {
        if (terms is null || terms.Count == 0)
        {
            return "";
        }

        string group = "";
        bool first = true;
        foreach (string term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            if (!first)
            {
                group = group + " AND ";
            }

            group = group + "NOT " + BuildTermClause(term, fields);
            first = false;
        }

        return group;
    }

    /// <summary>
    /// Builds the television-collection clause from the fixed curated collection set in
    /// <see cref="SearchDiscoveryCatalog"/>. The include variant is an <c>OR</c> group over that set
    /// (an item in any one of them qualifies); the exclude variant negates each member joined with
    /// <c>AND</c>. <see cref="VideoCollectionScope.AllWatchableVideo"/> emits nothing.
    ///
    /// These are collection-based labels, not a dependable movies-versus-television classification:
    /// the Internet Archive has no <c>mediatype:television</c>, and the separation leaks in both
    /// directions.
    /// </summary>
    private static string BuildVideoCollectionClause(VideoCollectionScope videoCollections)
    {
        if (videoCollections == VideoCollectionScope.AllWatchableVideo)
        {
            return "";
        }

        IReadOnlyList<string> collections = SearchDiscoveryCatalog.TelevisionCollections();
        bool including = videoCollections == VideoCollectionScope.TelevisionCollections;

        string group = "";
        bool first = true;
        foreach (string collection in collections)
        {
            if (!first)
            {
                group = group + (including ? " OR " : " AND ");
            }

            group = group + (including ? "" : "NOT ")
                + SearchDiscoveryCatalog.CollectionField + ":" + collection;
            first = false;
        }

        return including ? "(" + group + ")" : group;
    }

    /// <summary>
    /// The fixed exclusion expression appended when <see cref="SearchCriteria.ExcludeTrailers"/> is
    /// on. Every term is app-controlled literal text (never user input), so it needs no escaping.
    ///
    /// The rule is deliberately aggressive and is documented as such: it excludes items filed under
    /// the Internet Archive <c>movie_trailers</c> collection, items carrying the <c>trailers</c> or
    /// singular <c>trailer</c> subject, and any item whose title contains the word "trailer". Because
    /// the last clause is a plain title-field match, it also hides full movies and mixed uploads, and
    /// it hides titles that merely contain the word (for example <c>Trailer Park Boys</c>). It is not
    /// a promise to remove every teaser, TV spot, or game promo: those disappear only when they match
    /// these metadata/title rules. The Internet Archive has no single canonical "trailer" field, so
    /// trailers filed under unremarkable titles and untagged items remain.
    /// </summary>
    public const string TrailerExclusionClause =
        "NOT collection:\"movie_trailers\""
        + " AND NOT subject:\"trailers\""
        + " AND NOT subject:\"trailer\""
        + " AND NOT (title:\"trailer\" OR title:\"trailers\")";

    /// <summary>
    /// Builds the <c>year:</c> clause for the inclusive range endpoints. Equal endpoints produce
    /// the legacy <c>year:YYYY</c>; differing endpoints produce the inclusive range
    /// <c>year:[FROM TO TO]</c>; a missing endpoint is completed with the accepted bounds
    /// (1800 on the low side, the criteria's validated maximum on the high side).
    /// </summary>
    private static string BuildYearClause(SearchCriteria criteria)
    {
        if (criteria.IsExactYear)
        {
            return "year:" + criteria.YearFrom.ToString();
        }

        if (criteria.YearFrom is not null && criteria.YearTo is not null)
        {
            return "year:[" + criteria.YearFrom.ToString() + " TO " + criteria.YearTo.ToString() + "]";
        }

        if (criteria.YearFrom is not null)
        {
            int max = criteria.YearMax.HasValue ? criteria.YearMax.Value : 0;
            return "year:[" + criteria.YearFrom.ToString() + " TO " + max.ToString() + "]";
        }

        return "year:[1800 TO " + criteria.YearTo.ToString() + "]";
    }
/// <summary>
    /// Builds the <c>subject:</c> clause for the selected genres. Each selected genre key is resolved
    /// to its fixed, curated IA <c>subject</c> phrase from <see cref="GenreCatalog"/>, then safely
    /// escaped and quoted as a literal field phrase. Multiple genres are combined inside a
    /// parenthesized uppercase <c>OR</c> group so results match any selected genre; a single selected
    /// genre is emitted without redundant parentheses. The list is already in declared order and
    /// deduplicated, so duplicate selections cannot produce duplicate clauses and the output is
    /// deterministic.
    /// </summary>
    private static string BuildGenreClause(IReadOnlyList<string> selectedGenres)
    {
        string group = "";
        bool first = true;
        foreach (string key in selectedGenres)
        {
            string? phrase = GenreCatalog.SubjectPhraseFor(key);
            if (phrase is null)
            {
                continue; // unknown keys are never resolved to untrusted query text
            }
            if (!first)
            {
                group = group + " OR ";
            }
            group = group + "subject:\"" + EscapeLiteral(phrase) + "\"";
            first = false;
        }

        if (group.Length == 0)
        {
            return group;
        }
        if (selectedGenres.Count <= 1)
        {
            return group;
        }
        return "(" + group + ")";
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
