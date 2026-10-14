using System;
using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>Why a keyword/exclusion entry was rejected. Mapped to user-facing text by the UI layer.</summary>
public enum SearchKeywordParseError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>A quote was opened but never closed.</summary>
    UnterminatedQuote,

    /// <summary>A quoted phrase contained no text (<c>""</c>).</summary>
    EmptyQuotedPhrase,

    /// <summary>A quote character appeared inside an unquoted token (<c>un"quoted</c>).</summary>
    EmbeddedQuote,

    /// <summary>Non-delimiter text followed a closing quote (<c>"a"b</c>).</summary>
    TextAfterClosingQuote,

    /// <summary>A quote remained after an exact-phrase entry had already been read.</summary>
    UnexpectedQuoteInPhrase
}

/// <summary>
/// Pure parser for the keyword and excluded-terms boxes.
///
/// <para><b>Terms grammar (All / Any, and excluded terms).</b> Whitespace and commas delimit
/// unquoted terms. A quoted phrase is exactly one term. Quotes must delimit a whole phrase: a quote
/// inside an unquoted token, text immediately after a closing quote, an unterminated quote, and an
/// empty quoted phrase are all rejected.</para>
///
/// <para><b>Exact-phrase grammar.</b> The whole entry is one phrase. Outer whitespace is trimmed and
/// one matching outer quote pair is optionally consumed; internal commas are literal. Any remaining
/// quote character is rejected. There is no quote-nesting and no quote-escape syntax in this
/// slice.</para>
///
/// <para><b>Rejection, never reinterpretation.</b> Invalid input is rejected rather than silently
/// coerced into a different search, so the caller can block submission and ask for a correction. A
/// rejected entry never reaches the query builder.</para>
///
/// <para>This type is WPF-independent and deterministic so the grammar can be unit-tested directly.</para>
/// </summary>
public static class SearchKeywordPhraseParser
{
    /// <summary>The delimiter characters that separate unquoted terms.</summary>
    private const char Comma = ',';

    /// <summary>The phrase delimiter.</summary>
    private const char Quote = '"';

    /// <summary>
    /// Parses a terms entry into individual terms. Returns true and an empty list for empty or
    /// delimiter-only input; returns false with a specific <paramref name="error"/> for rejected
    /// input, in which case <paramref name="terms"/> is null and must not be used.
    /// </summary>
    public static bool TryParseTerms(
        string? input,
        out IReadOnlyList<string>? terms,
        out SearchKeywordParseError error)
    {
        terms = null;
        error = SearchKeywordParseError.None;

        string text = input ?? "";
        var parsed = new List<string>();

        int index = 0;
        while (index < text.Length)
        {
            if (IsDelimiter(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == Quote)
            {
                index++; // consume the opening quote
                int contentStart = index;
                int closing = index;
                while (closing < text.Length && text[closing] != Quote)
                {
                    closing++;
                }

                if (closing >= text.Length)
                {
                    error = SearchKeywordParseError.UnterminatedQuote;
                    return false;
                }

                string phrase = text.Substring(contentStart, closing - contentStart);
                if (IsBlank(phrase))
                {
                    error = SearchKeywordParseError.EmptyQuotedPhrase;
                    return false;
                }

                parsed.Add(phrase);
                index = closing + 1;

                if (index < text.Length && !IsDelimiter(text[index]))
                {
                    error = SearchKeywordParseError.TextAfterClosingQuote;
                    return false;
                }

                continue;
            }

            int tokenStart = index;
            while (index < text.Length && !IsDelimiter(text[index]))
            {
                if (text[index] == Quote)
                {
                    error = SearchKeywordParseError.EmbeddedQuote;
                    return false;
                }

                index++;
            }

            string token = text.Substring(tokenStart, index - tokenStart);
            if (token.Length > 0)
            {
                parsed.Add(token);
            }
        }

        terms = parsed;
        return true;
    }

    /// <summary>
    /// Parses an exact-phrase entry. Returns true with <paramref name="phrase"/> set to null for
    /// empty or delimiter-only input; returns false with a specific <paramref name="error"/> for
    /// rejected input, in which case <paramref name="phrase"/> is null and must not be used.
    /// </summary>
    public static bool TryParseExactPhrase(
        string? input,
        out string? phrase,
        out SearchKeywordParseError error)
    {
        phrase = null;
        error = SearchKeywordParseError.None;

        string trimmed = (input ?? "").Trim();

        // Whitespace-only or delimiter-only input is empty, not a phrase.
        bool onlyDelimiters = true;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!IsDelimiter(trimmed[i]))
            {
                onlyDelimiters = false;
                break;
            }
        }

        if (onlyDelimiters)
        {
            return true;
        }

        if (trimmed.Length >= 2 && trimmed[0] == Quote && trimmed[trimmed.Length - 1] == Quote)
        {
            string inner = trimmed.Substring(1, trimmed.Length - 2);
            if (IsBlank(inner))
            {
                error = SearchKeywordParseError.EmptyQuotedPhrase;
                return false;
            }

            if (inner.IndexOf(Quote) >= 0)
            {
                error = SearchKeywordParseError.UnexpectedQuoteInPhrase;
                return false;
            }

            phrase = inner;
            return true;
        }

        if (trimmed.IndexOf(Quote) >= 0)
        {
            error = SearchKeywordParseError.UnexpectedQuoteInPhrase;
            return false;
        }

        phrase = trimmed;
        return true;
    }

    /// <summary>True for a delimiter: whitespace or comma.</summary>
    private static bool IsDelimiter(char c)
    {
        return c == Comma || IsWhitespace(c);
    }

    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
    }

    private static bool IsBlank(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (!IsWhitespace(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}