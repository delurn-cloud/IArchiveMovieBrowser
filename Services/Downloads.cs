using System;

namespace IArchiveMovieBrowser.Services;

/// <summary>Outcome of a single selected-file video download.</summary>
public enum VideoDownloadOutcome
{
    /// <summary>The streamed transfer was safely finalized into the destination.</summary>
    Completed,

    /// <summary>The caller's token requested cancellation before completion.</summary>
    Cancelled,

    /// <summary>The server refused access (401/403).</summary>
    Restricted,

    /// <summary>The server returned 404.</summary>
    Missing,

    /// <summary>An otherwise non-success HTTP response was received.</summary>
    NonSuccessHttp,

    /// <summary>The final redirect URI is not a trusted IA delivery location.</summary>
    UnsafeFinalUri,

    /// <summary>A 2xx response whose Content-Type is clearly non-media/error-like.</summary>
    NonMedia,

    /// <summary>The destination exists but replacement was not explicitly confirmed.</summary>
    DestinationExists,

    /// <summary>A local file-system/destination problem prevented finalization.</summary>
    FileSystemFailure,

    /// <summary>The request/TLS/network failed.</summary>
    NetworkFailure
}

/// <summary>Request to download precisely one selected, validated IA video file.</summary>
public sealed class VideoDownloadRequest
{
    public Uri SourceUri { get; }
    public string FinalPath { get; }
    public bool ReplaceConfirmed { get; }

    public VideoDownloadRequest(Uri sourceUri, string finalPath, bool replaceConfirmed)
    {
        if (sourceUri is null)
        {
            throw new ArgumentNullException(nameof(sourceUri));
        }
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            throw new ArgumentException("Final path must not be blank.", nameof(finalPath));
        }
        SourceUri = sourceUri;
        FinalPath = finalPath;
        ReplaceConfirmed = replaceConfirmed;
    }
}

/// <summary>Progress of an in-flight video download.</summary>
public sealed class DownloadProgress
{
    public long BytesDownloaded { get; }
    public long? TotalBytes { get; }

    public DownloadProgress(long bytesDownloaded, long? totalBytes)
    {
        BytesDownloaded = bytesDownloaded < 0 ? 0 : bytesDownloaded;
        TotalBytes = totalBytes;
    }

    public bool HasKnownTotal => TotalBytes is not null && TotalBytes! > 0;

    public int Percent => HasKnownTotal ? (int)(BytesDownloaded * 100 / TotalBytes!) : 0;
}

/// <summary>Receives throttled download progress from the transfer loop.</summary>
public interface DownloadProgressListener
{
    void OnProgress(DownloadProgress progress);
}

/// <summary>Structured result of a video download attempt.</summary>
public sealed class VideoDownloadResult
{
    public VideoDownloadOutcome Outcome { get; }
    public Uri? FinalUri { get; }
    public System.Net.HttpStatusCode? StatusCode { get; }
    public Exception? Cause { get; }

    public bool Succeeded => Outcome == VideoDownloadOutcome.Completed;

    private VideoDownloadResult(
        VideoDownloadOutcome outcome,
        Uri? finalUri,
        System.Net.HttpStatusCode? statusCode,
        Exception? cause)
    {
        Outcome = outcome;
        FinalUri = finalUri;
        StatusCode = statusCode;
        Cause = cause;
    }

    public static VideoDownloadResult Completed() =>
        new VideoDownloadResult(VideoDownloadOutcome.Completed, null, null, null);

    public static VideoDownloadResult Cancelled() =>
        new VideoDownloadResult(VideoDownloadOutcome.Cancelled, null, null, null);

    public static VideoDownloadResult Restricted() =>
        new VideoDownloadResult(VideoDownloadOutcome.Restricted, null, System.Net.HttpStatusCode.Unauthorized, null);

    public static VideoDownloadResult Missing() =>
        new VideoDownloadResult(VideoDownloadOutcome.Missing, null, System.Net.HttpStatusCode.NotFound, null);

    public static VideoDownloadResult NonSuccess(System.Net.HttpStatusCode status) =>
        new VideoDownloadResult(VideoDownloadOutcome.NonSuccessHttp, null, status, null);

    public static VideoDownloadResult UnsafeFinal(Uri? finalUri) =>
        new VideoDownloadResult(VideoDownloadOutcome.UnsafeFinalUri, finalUri, null, null);

    public static VideoDownloadResult NonMedia() =>
        new VideoDownloadResult(VideoDownloadOutcome.NonMedia, null, null, null);

    public static VideoDownloadResult DestinationExists() =>
        new VideoDownloadResult(VideoDownloadOutcome.DestinationExists, null, null, null);

    public static VideoDownloadResult FileSystemFailure(Exception? cause = null) =>
        new VideoDownloadResult(VideoDownloadOutcome.FileSystemFailure, null, null, cause);

    public static VideoDownloadResult NetworkFailure(Exception? cause = null) =>
        new VideoDownloadResult(VideoDownloadOutcome.NetworkFailure, null, null, cause);
}

/// <summary>
/// Pure filename/naming/progress/display helpers and exact status text for the Details-window
/// single-file video download. WPF- and network-free so it can be unit-tested.
/// </summary>
public static class Downloads
{
    public const string DownloadButtonLabel = "Download selected video…";
    public const string CancelDownloadButtonLabel = "Cancel download";
    public const string NoSelectionText = "Select a playable video file to download.";
    public const string PreparingText = "Preparing video download…";
    public const string CompleteText = "Download complete.";
    public const string CancelledText = "Download cancelled.";
    public const string RestrictedText =
        "Internet Archive did not make this file available for download.";
    public const string NotFoundText =
        "Internet Archive could not find this selected file.";
    public const string UnsafeFinalText =
        "The video link resolved to an unexpected location and was not downloaded.";
    public const string NonMediaText =
        "Internet Archive did not return a downloadable video file.";
    public const string DestinationExistsText =
        "A file with this name already exists.";
    public const string FileSystemFailureText =
        "Could not save the video to the selected location.";
    public const string NetworkFailureText =
        "Could not download the selected video. Check your connection and try again.";
    public const string ReplacePromptText = "A file with this name already exists.\n\nReplace it?";
    public const string ReplaceButtonText = "Replace";
    public const string ReplaceCancelButtonText = "Cancel";
    public const string SaveDialogFilter =
        "Video files|*.mp4;*.m4v;*.webm;*.mkv;*.avi;*.mov;*.mpeg;*.mpg|All files|*.*";

    /// <summary>A user's overwrite-confirmation decision (UI-independent).</summary>
    public enum OverwriteDecision
    {
        /// <summary>Explicit affirmative approval to replace an existing destination.</summary>
        Authorize,

        /// <summary>No, close, Escape, or any other non-affirmative outcome (safe default).</summary>
        Deny
    }

    /// <summary>
    /// True only for an explicit affirmative overwrite decision. Every other outcome authorizes
    /// no request, no overwrite, and no final-file mutation.
    /// </summary>
    public static bool ShouldReplace(OverwriteDecision? decision) =>
        decision == OverwriteDecision.Authorize;

    public static string OtherNonSuccessText(System.Net.HttpStatusCode status) =>
        "Internet Archive returned HTTP " + ((int)status).ToString() + " for this selected file.";

    /// <summary>Sanitizes a suggested file name to be safe as a single Windows file name.</summary>
    public static string? SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        string cleaned = name.Trim();
        var builder = new System.Text.StringBuilder(cleaned.Length);
        foreach (char c in cleaned)
        {
            if (c == '<' || c == '>' || c == ':' || c == '"' || c == '/' || c == '\\'
                || c == '|' || c == '?' || c == '*' || c < ' ')
            {
                continue;
            }
            builder.Append(c);
        }
        string result = builder.ToString();
        while (result.Length > 0
            && (result[result.Length - 1] == '.' || result[result.Length - 1] == ' '))
        {
            result = result.Substring(0, result.Length - 1);
        }
        return result.Length == 0 || result == "." || result == ".." ? null : result;
    }

    public static bool IsUnsafeRawFileName(string? name)
    {
        return name is null
            || string.IsNullOrWhiteSpace(name)
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains("..", StringComparison.Ordinal);
    }
/// <summary>
    /// Suggested Save-dialog file name derived from the selected Internet Archive filename,
    /// sanitized for Windows, with a safe default when nothing usable remains. Never returns null.
    /// </summary>
    public static string SuggestedFileName(string? rawSelectedFileName)
    {
        string? sanitized = SanitizeFileName(rawSelectedFileName);
        if (sanitized is not null)
        {
            return sanitized;
        }
        return "video.clip" + SafeExtension(rawSelectedFileName);
    }

    private static string SafeExtension(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ".mp4";
        }
        string ext = System.IO.Path.GetExtension(name);
        return string.IsNullOrWhiteSpace(ext) ? ".mp4" : ext; // Path.GetExtension is extension-only, not a path
    }

    /// <summary>Collision-safe sibling temp path: <c>final.partial</c> (or a numbered variant).</summary>
    public static string TempSiblingPath(string finalPath)
    {
        string candidate = finalPath + ".partial";
        int n = 1;
        while (System.IO.File.Exists(candidate))
        {
            candidate = finalPath + "." + n.ToString() + ".partial";
            n++;
        }
        return candidate;
    }

    /// <summary>Collision-safe sibling backup path for a confirmed Replace.</summary>
    public static string BackupSiblingPath(string finalPath)
    {
        string candidate = finalPath + ".iamb.bak";
        int n = 1;
        while (System.IO.File.Exists(candidate))
        {
            candidate = finalPath + "." + n.ToString() + ".iamb.bak";
            n++;
        }
        return candidate;
    }

    /// <summary>Human-readable byte count (binary units, consistent app-wide).</summary>
    public static string FormatBytes(long bytes) => DetailsDisplayText.SizeText(bytes);

    /// <summary>Concise visible progress text for a known total.</summary>
    public static string ProgressKnownText(DownloadProgress progress)
    {
        long total = progress.TotalBytes.HasValue ? progress.TotalBytes.Value : 0;
        return "Downloading… " + progress.Percent.ToString() + "% ("
            + FormatBytes(progress.BytesDownloaded) + " of " + FormatBytes(total) + ")";
    }

    /// <summary>Concise visible progress text when the total size is unknown.</summary>
    public static string ProgressUnknownText(DownloadProgress progress) =>
        "Downloading… " + FormatBytes(progress.BytesDownloaded);

    /// <summary>Chooses visible progress text depending on whether the total is known.</summary>
    public static string ProgressText(DownloadProgress progress) =>
        progress.HasKnownTotal ? ProgressKnownText(progress) : ProgressUnknownText(progress);

    /// <summary>Maps a download result to a single user-visible status line.</summary>
    public static string OutcomeText(VideoDownloadResult result)
    {
        switch (result.Outcome)
        {
            case VideoDownloadOutcome.Cancelled:
                return CancelledText;
            case VideoDownloadOutcome.Restricted:
                return RestrictedText;
            case VideoDownloadOutcome.Missing:
                return NotFoundText;
            case VideoDownloadOutcome.UnsafeFinalUri:
                return UnsafeFinalText;
            case VideoDownloadOutcome.NonMedia:
                return NonMediaText;
            case VideoDownloadOutcome.DestinationExists:
                return DestinationExistsText;
            case VideoDownloadOutcome.FileSystemFailure:
                return FileSystemFailureText;
            case VideoDownloadOutcome.NetworkFailure:
                return NetworkFailureText;
            case VideoDownloadOutcome.Completed:
                return CompleteText;
            case VideoDownloadOutcome.NonSuccessHttp:
            default:
                return OtherNonSuccessText(
                    result.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
        }
    }
}
