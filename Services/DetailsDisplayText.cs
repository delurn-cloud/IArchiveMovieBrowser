using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Pure, WPF-independent display logic for the item Details window. Reuses the committed
/// <see cref="InternetArchiveDownloadUrlBuilder"/> classification core rather than
/// duplicating extension sets.
/// </summary>
public static class DetailsDisplayText
{
    private const string Separator = " · ";

    public const string MissingText = "(not supplied)";
    public const string UnknownTypeText = "(unknown type)";
    public const string UnknownKindText = "(other)";
    public const string UnknownSizeText = "(unknown size)";
    public const string MediaCandidateYesText = "Yes";
    public const string MediaCandidateNoText = "No";
    public const string ViewLicenseLabel = "View license";

    // --- Metadata fallbacks / joined value helpers --------------------------------

    public static string TitleText(InternetArchiveItemMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.Title) ? MissingText : metadata.Title;

    public static string DateYearText(InternetArchiveItemMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Date))
        {
            return metadata.Date;
        }
        if (!string.IsNullOrWhiteSpace(metadata.Year))
        {
            return metadata.Year;
        }
        return MissingText;
    }

    public static string MediaTypeText(InternetArchiveItemMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.MediaType) ? UnknownTypeText : metadata.MediaType;

    public static string JoinedText(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return MissingText;
        }

        string text = "";
        bool first = true;
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (!first)
            {
                text = text + ", ";
            }
            text = text + value;
            first = false;
        }
        return text.Length == 0 ? MissingText : text;
    }

    /// <summary>License display: a readable "View license" label plus the actual URL.</summary>
    public static string LicenseText(InternetArchiveItemMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.LicenseUrl))
        {
            return MissingText;
        }
        return ViewLicenseLabel + " — " + metadata.LicenseUrl;
    }

    // --- File display (classification reuses the committed core) ------------------

    public static string KindText(InternetArchiveRemoteFile file)
    {
        InternetArchiveFileKind kind = InternetArchiveDownloadUrlBuilder.ClassifyFileKind(file.Name);
        return kind switch
        {
            InternetArchiveFileKind.Video => "Video",
            InternetArchiveFileKind.Audio => "Audio",
            _ => UnknownKindText
        };
    }

    public static string MediaCandidateText(InternetArchiveRemoteFile file)
    {
        bool candidate = IsMediaCandidate(file);
        return candidate ? MediaCandidateYesText : MediaCandidateNoText;
    }

    public static bool IsMediaCandidate(InternetArchiveRemoteFile file)
    {
        InternetArchiveFileKind kind = InternetArchiveDownloadUrlBuilder.ClassifyFileKind(file.Name);
        return kind switch
        {
            InternetArchiveFileKind.Video => true,
            InternetArchiveFileKind.Audio => true,
            _ => false
        };
    }

    public static string SizeText(long? size)
    {
        if (size is null)
        {
            return UnknownSizeText;
        }

        long bytes = (long)size;
        if (bytes < 1024)
        {
            return bytes.ToString() + " B";
        }
        if (bytes < 1024L * 1024L)
        {
            return (bytes / 1024.0).ToString("#.#") + " KB";
        }
        if (bytes < 1024L * 1024L * 1024L)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("#.#") + " MB";
        }
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("#.#") + " GB";
    }

    /// <summary>True when at least one entry is a video/audio media candidate.</summary>
    public static bool AnyMediaCandidate(IReadOnlyList<InternetArchiveRemoteFile> files)
    {
        if (files is null)
        {
            return false;
        }
        foreach (InternetArchiveRemoteFile file in files)
        {
            if (IsMediaCandidate(file))
            {
                return true;
            }
        }
        return false;
    }

    // --- Status texts -------------------------------------------------------------

    public static string StatusLoadingDetails() => "Loading details…";
    public static string StatusReadyDetails() => "Ready";
    public static string StatusNoUsableMedia() => "No usable media files found.";
    public static string StatusError(string message) => "Error: " + message;
    public static string StatusCancelled() => "Cancelled";
}