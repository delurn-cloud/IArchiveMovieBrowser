namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Which Internet Archive metadata fields a positive keyword term (or an excluded term) is matched
/// against.
///
/// Keywords are <b>literal</b>. There is no stemming, no synonym expansion, and no automatic
/// broadening: each term is matched as the phrase the user typed, against the selected fields only.
/// A keyword match means an uploader recorded that text in the field; it is not a claim of verified
/// classification, because Internet Archive subject and description vocabulary is self-reported and
/// inconsistent.
/// </summary>
public enum SearchKeywordFields
{
    /// <summary>
    /// Default. Match each term against the Internet Archive <c>subject</c> field only. Subjects are
    /// a controlled, list-valued vocabulary, so this is the higher-precision choice.
    /// </summary>
    Subjects,

    /// <summary>
    /// Match each term against <c>subject</c> <b>or</b> <c>description</c>. This widens recall,
    /// because <c>description</c> is unstructured free text that also covers books, newspapers and
    /// unrelated uploads; the per-term OR is emitted explicitly so the widening is inspectable in the
    /// generated query.
    /// </summary>
    SubjectsAndDescriptions
}