namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Broad classification of an Internet Archive file based on its filename extension.
/// Intentionally independent of any UI, networking, playback, download, or account logic.
/// </summary>
public enum InternetArchiveFileKind
{
    /// <summary>File extension commonly indicates viewable video content.</summary>
    Video,

    /// <summary>File extension commonly indicates listenable audio content.</summary>
    Audio,

    /// <summary>File extension is not recognized as video or audio.</summary>
    Other
}