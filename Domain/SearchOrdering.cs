namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Result ordering applied to an Internet Archive search.
///
/// Every option is expressed as <c>sort[]</c> request parameters appended alongside the query, and
/// carried inside the immutable <see cref="SearchCriteria"/> so paging and refresh re-issue the
/// identical ordering rather than silently reverting to the server default.
/// </summary>
public enum SearchOrdering
{
    /// <summary>
    /// Default. No sort parameter is sent at all, so the Internet Archive's own ordering is used.
    /// This is deliberately <b>not</b> labelled "Relevance": the semantics of the Internet Archive's
    /// default ordering are not verified, so it is named for what it actually is.
    /// </summary>
    InternetArchiveDefault,

    /// <summary>
    /// Highest download counts first, with an ascending identifier tiebreaker so that equal download
    /// counts resolve deterministically within a single ordering.
    ///
    /// The tiebreaker makes ties inside one ordering stable. It is <b>not</b> a snapshot guarantee:
    /// the endpoint offers no frozen result set, so paging can still shift if items are added,
    /// removed or re-downloaded between page requests.
    /// </summary>
    MostDownloaded
}