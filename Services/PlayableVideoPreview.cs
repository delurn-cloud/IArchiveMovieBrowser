using System;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Resolves the direct Internet Archive stream/download URL for a selected playable video
/// candidate. It reuses the committed <see cref="InternetArchiveDownloadUrlBuilder"/> so
/// there is a single, canonical URL format and escaping rule.
///
/// Pure and WPF-independent. Unlike the underlying builder, this hardens the URL resolution
/// for UI display: it never throws on blank/invalid input and instead yields a placeholder.
/// </summary>
public static class PlayableVideoPreview
{
    public const string SelectionPlaceholderText =
        "Select a playable video file to preview its direct stream URL.";

    /// <summary>The read-only preview shown before/without a valid selection.</summary>
    public static string SelectionPlaceholder() => SelectionPlaceholderText;

    /// <summary>
    /// Returns the direct IA file URL for the given item identifier and filename, or the
    /// selection placeholder when the input is blank/invalid. Never throws.
    /// </summary>
    public static string Build(string? identifier, string? filename)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(filename))
        {
            return SelectionPlaceholderText;
        }

        try
        {
            Uri uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);
            return uri.ToString();
        }
        catch (ArgumentException)
        {
            return SelectionPlaceholderText;
        }
        catch (InvalidOperationException)
        {
            return SelectionPlaceholderText;
        }
    }
}