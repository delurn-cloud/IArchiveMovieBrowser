using IArchiveMovieBrowser.Services;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// Describes a single publicly accessible file within an Internet Archive item, together
/// with the canonical HTTPS URL used to stream or download that file.
///
/// This type is deliberately free of any UI, networking, playback, download, or account
/// dependencies so it can be reasoned about and unit-tested in isolation.
/// </summary>
public sealed class InternetArchiveFileDescriptor
{
    /// <summary>The Internet Archive item identifier (a single URL-safe segment).</summary>
    public string Identifier { get; }

    /// <summary>The original filename exactly as provided by the caller (a single segment).</summary>
    public string OriginalFilename { get; }

    /// <summary>The canonical absolute HTTPS download URI for this file.</summary>
    public Uri DownloadUri { get; }

    /// <summary>Classification of the file derived from its extension.</summary>
    public InternetArchiveFileKind Kind { get; }

    /// <summary>
    /// True when the file is classified as either video or audio, i.e. eligible to surface
    /// as a candidate for the app's watch/download flows.
    /// </summary>
    public bool IsMediaCandidate { get; }

    internal InternetArchiveFileDescriptor(
        string identifier,
        string originalFilename,
        Uri downloadUri,
        InternetArchiveFileKind kind)
    {
        Identifier = identifier;
        OriginalFilename = originalFilename;
        DownloadUri = downloadUri;
        Kind = kind;
        IsMediaCandidate = kind is InternetArchiveFileKind.Video or InternetArchiveFileKind.Audio
            ? true
            : false;
    }
}