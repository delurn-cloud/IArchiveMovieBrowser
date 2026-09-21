using System;
using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Conservative, deterministic classifier that identifies likely direct, playable video
/// files from an Internet Archive item's existing file metadata. The authoritative
/// first-slice rule is the filename extension; IA format labels are not relied on.
///
/// Pure and WPF-independent so it can be unit-tested without UI automation.
/// </summary>
public static class PlayableVideoClassifier
{
    /// <summary>
    /// Filename extensions treated as likely playable video candidates, case-insensitively.
    /// </summary>
    public static readonly IReadOnlySet<string> PlayableVideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mkv", ".avi", ".mov",
            ".wmv", ".webm", ".mpg", ".mpeg",
            ".m2ts", ".ts", ".ogv", ".3gp"
        };

    /// <summary>
    /// Returns the subset of files that look like playable video, deduplicated by filename
    /// (case-insensitive) and preserving the incoming source order. Null/blank names and
    /// malformed entries are skipped without throwing.
    /// </summary>
    public static IReadOnlyList<InternetArchiveRemoteFile> SelectPlayableCandidates(
        IReadOnlyList<InternetArchiveRemoteFile>? files)
    {
        if (files is null || files.Count == 0)
        {
            return new List<InternetArchiveRemoteFile>();
        }

        var result = new List<InternetArchiveRemoteFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (InternetArchiveRemoteFile file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Name))
            {
                continue;
            }

            string name = file.Name.Trim();
            if (seen.Contains(name))
            {
                continue;
            }

            string extension = System.IO.Path.GetExtension(name);
            if (PlayableVideoExtensions.Contains(extension))
            {
                result.Add(file);
                seen.Add(name);
            }
        }

        return result;
    }
}