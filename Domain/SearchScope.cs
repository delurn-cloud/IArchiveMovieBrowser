namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Broad search scope for the Internet Archive catalog. The launcher lets the user pick a
/// scope; this drives how the advanced-search query is restricted (video-only,
/// non-video, or unrestricted). The enum is intentionally extensible.
/// </summary>
public enum SearchScope
{
    /// <summary>Restrict to Internet Archive video (mediatype:movies).</summary>
    WatchableVideo,

    /// <summary>Exclude Internet Archive video; non-video items matching the search.</summary>
    RelatedMaterials,

    /// <summary>No media-type restriction; all matching Internet Archive items.</summary>
    Everything
}