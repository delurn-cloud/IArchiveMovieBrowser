namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// How the individual parsed keyword terms are combined into one clause.
///
/// The choice applies only to the positive keyword box. Excluded terms are always combined with
/// <c>AND</c>, because "exclude these words" means an item must match <b>none</b> of them: a
/// <c>NOT (a OR b)</c> group would be a no-op that widens results instead of narrowing them.
/// </summary>
public enum SearchKeywordMatchMode
{
    /// <summary>Default. Every term must match: a parenthesized <c>AND</c> group.</summary>
    AllTerms,

    /// <summary>At least one term must match: a parenthesized <c>OR</c> group.</summary>
    AnyTerms,

    /// <summary>
    /// The entire entry is one phrase. This mode never tokenizes: the whole box content becomes a
    /// single term, so it produces the same clause as typing the same text inside one quote pair.
    /// </summary>
    ExactPhrase
}