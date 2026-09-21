using System.IO;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Builds canonical, single-segment download URLs for publicly accessible Internet Archive
/// files and produces lightweight descriptors used elsewhere in the application.
///
/// The logic here is pure and deterministic: it performs no network access, no download
/// execution, and depends on no WPF UI, playback, or account functionality.
/// </summary>
public static class InternetArchiveDownloadUrlBuilder
{
    private static readonly Uri ArchiveOrgDownloadRoot =
        new("https://archive.org/download/", UriKind.Absolute);

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".webm", ".avi", ".mov", ".mpg", ".mpeg", ".m4v", ".ogv"
        };

    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".aac"
        };

    /// <summary>
    /// Builds the canonical download URI for an Internet Archive item/identifier and filename,
    /// percent-encoding each segment separately.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when either value is null, empty, whitespace-only, or represents a multi-segment
    /// path rather than a single identifier/filename segment.
    /// </exception>
    public static Uri BuildDownloadUri(string? identifier, string? filename)
    {
        var safeIdentifier = ValidateSingleSegment(identifier, nameof(identifier));
        var safeFilename = ValidateSingleSegment(filename, nameof(filename));

        var escapedIdentifier = Uri.EscapeDataString(safeIdentifier);
        var escapedFilename = Uri.EscapeDataString(safeFilename);

        var absolute = new Uri(ArchiveOrgDownloadRoot, $"{escapedIdentifier}/{escapedFilename}");

        if (!string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected an HTTPS download URI but produced '{absolute}'.");
        }

        if (!string.Equals(absolute.Host, "archive.org", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected host 'archive.org' but produced '{absolute}'.");
        }

        return absolute;
    }

    /// <summary>
    /// Builds a full descriptor for an Internet Archive identifier/filename, including the
    /// canonical download URI, extension classification, and media-candidate flag.
    /// </summary>
    public static InternetArchiveFileDescriptor CreateDescriptor(
        string? identifier,
        string? filename)
    {
        var uri = BuildDownloadUri(identifier, filename);
        var safeIdentifier = ValidateSingleSegment(identifier, nameof(identifier));
        var safeFilename = ValidateSingleSegment(filename, nameof(filename));

        var kind = ClassifyFileKind(safeFilename);
        return new InternetArchiveFileDescriptor(safeIdentifier, safeFilename, uri, kind);
    }

    /// <summary>
    /// Classifies a filename by its extension (case-insensitively) into a video, audio,
    /// or other category.
    /// </summary>
    public static InternetArchiveFileKind ClassifyFileKind(string filename)
    {
        if (filename is null)
        {
            throw new ArgumentNullException(nameof(filename));
        }

        var extension = Path.GetExtension(filename);
        if (VideoExtensions.Contains(extension))
        {
            return InternetArchiveFileKind.Video;
        }

        if (AudioExtensions.Contains(extension))
        {
            return InternetArchiveFileKind.Audio;
        }

        return InternetArchiveFileKind.Other;
    }

    /// <summary>
    /// Validates that an identifier/filename is a single, non-empty, non-path segment.
    /// No narrow "safe character" rules are imposed: spaces, parentheses, apostrophes,
    /// Unicode, and multiple dots are all acceptable as long as the value stays a single
    /// segment free of traversal or absolute-URL semantics.
    /// </summary>
    private static string ValidateSingleSegment(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{parameterName} must not be null, empty, or whitespace-only.",
                parameterName);
        }

        if (value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException(
                $"{parameterName} must be a single segment and must not contain '/' or '\\'.",
                parameterName);
        }

        if (value.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{parameterName} must not contain '..'.",
                parameterName);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"{parameterName} must not be a full HTTP(S) URL.",
                parameterName);
        }

        return value;
    }
}