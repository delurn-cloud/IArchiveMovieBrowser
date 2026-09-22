using System;
using System.Net.Http;

namespace IArchiveMovieBrowser.Services;

/// <summary>The overall outcome category of a probed direct video link.</summary>
public enum DirectLinkProbeKind
{
    /// <summary>A response was received (headers captured; body not read).</summary>
    Response,

    /// <summary>The request/TLS/network failed; no response was received.</summary>
    NetworkFailure,

    /// <summary>The calling token requested cancellation before a response was captured.</summary>
    Cancelled
}

/// <summary>
/// Immutable snapshot of the HTTP characteristics of a probed direct video link, captured
/// without downloading/reading the response body.
/// </summary>
public sealed class DirectLinkProbe
{
    public DirectLinkProbeKind Kind { get; }
    public Uri? RequestedUri { get; }
    public Uri? FinalUri { get; }
    public System.Net.HttpStatusCode StatusCode { get; }
    public string? ReasonPhrase { get; }
    public string? ContentType { get; }
    public long? ContentLength { get; }
    public string? ContentDisposition { get; }

    /// <summary>True when the request/TLS/network failed (no response, not a user cancellation).</summary>
    public bool NetworkFailed => Kind == DirectLinkProbeKind.NetworkFailure;

    /// <summary>True when the calling token requested cancellation before a response was captured.</summary>
    public bool IsCancelled => Kind == DirectLinkProbeKind.Cancelled;

    /// <summary>True when a response was received (i.e. not a transport/TLS/network failure).</summary>
    public bool GotResponse => Kind == DirectLinkProbeKind.Response;

    public bool IsSuccessStatus =>
        Kind == DirectLinkProbeKind.Response && StatusCodeNumber >= 200 && StatusCodeNumber < 300;

    /// <summary>Integer HTTP status code (for display/comparison).</summary>
    public int StatusCodeNumber => (int)StatusCode;

    private DirectLinkProbe(
        DirectLinkProbeKind kind,
        Uri? requestedUri,
        Uri? finalUri,
        System.Net.HttpStatusCode statusCode,
        string? reasonPhrase,
        string? contentType,
        long? contentLength,
        string? contentDisposition)
    {
        Kind = kind;
        RequestedUri = requestedUri;
        FinalUri = finalUri;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ContentType = contentType;
        ContentLength = contentLength;
        ContentDisposition = contentDisposition;
    }

    internal static DirectLinkProbe NetworkError(Uri? requestedUri)
        => new DirectLinkProbe(DirectLinkProbeKind.NetworkFailure, requestedUri, null,
            System.Net.HttpStatusCode.NotFound, null, null, null, null);

    internal static DirectLinkProbe Cancelled(Uri? requestedUri)
        => new DirectLinkProbe(DirectLinkProbeKind.Cancelled, requestedUri, null,
            System.Net.HttpStatusCode.NotFound, null, null, null, null);

    public static DirectLinkProbe FromResponse(
        Uri requestedUri,
        Uri? finalUri,
        System.Net.HttpStatusCode statusCode,
        string? reasonPhrase,
        string? contentType,
        long? contentLength,
        string? contentDisposition)
        => new DirectLinkProbe(DirectLinkProbeKind.Response, requestedUri, finalUri, statusCode,
            reasonPhrase, contentType, contentLength, contentDisposition);
}
/// <summary>
/// Pure display text for the selected-video-link diagnostic. Exact user-visible status wording
/// plus a compact, copyable multi-line summary. Never exposes raw exception detail.
/// </summary>
public static class DirectLinkDiagnosticText
{
    /// <summary>Exact command label for the diagnostic button.</summary>
    public const string CheckButtonLabel = "Check selected video link";

    /// <summary>Shown when no playable file is selected (no request made).</summary>
    public const string NoSelectionText = "Select a playable video file to check its link.";

    /// <summary>Exact text shown while the request is in flight.</summary>
    public const string CheckingText = "Checking selected video link…";

    /// <summary>2xx but missing/non-video Content-Type.</summary>
    public const string NotIdentifiedAsVideoText =
        "Link responded, but Internet Archive did not identify it as video.";

    /// <summary>HTTP 401/403 (access restricted).</summary>
    public const string RestrictedText =
        "Internet Archive did not make this file available for playback or download.";

    /// <summary>HTTP 404 (file not found).</summary>
    public const string NotFoundText =
        "Internet Archive could not find this selected file.";

    /// <summary>Transport/TLS/request failure.</summary>
    public const string NetworkFailureText =
        "Could not check the selected video link. Check your connection and try again.";

    /// <summary>Shown when the check was cancelled (never the network-failure wording).</summary>
    public const string CancelledText = "Link check cancelled.";

    /// <summary>2xx with a video-like Content-Type and a known Content-Length.</summary>
    public static string SuccessText(DirectLinkProbe probe, System.Net.HttpStatusCode status, string? type, long? length)
    {
        string text = "Video link is available — HTTP " + ((int)status).ToString();
        if (!string.IsNullOrWhiteSpace(type))
        {
            text = text + ", " + type;
        }
        if (length is not null && length > 0)
        {
            text = text + ", " + DetailsDisplayText.SizeText(length);
        }
        return text + ".";
    }

    /// <summary>Other non-success status wording.</summary>
    public static string OtherNonSuccessText(System.Net.HttpStatusCode status) =>
        "Internet Archive returned HTTP " + ((int)status).ToString() + " for this selected file.";

    /// <summary>True when the Content-Type indicates a video-like media response.</summary>
    public static bool IsVideoLikeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }
        string lower = contentType.ToLower();
        return lower.StartsWith("video/") || lower == "application/mp4";
    }

    /// <summary>Chooses the single user-visible status line for a probe result.</summary>
    public static string OutcomeText(DirectLinkProbe probe)
    {
        if (probe.NetworkFailed)
        {
            return NetworkFailureText;
        }

        if (probe.IsCancelled)
        {
            return CancelledText;
        }

        if (probe.StatusCode == System.Net.HttpStatusCode.Unauthorized
            || probe.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return RestrictedText;
        }

        if (probe.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFoundText;
        }

        if (probe.IsSuccessStatus)
        {
            if (!IsVideoLikeContentType(probe.ContentType))
            {
                return NotIdentifiedAsVideoText;
            }
            return SuccessText(probe, probe.StatusCode, probe.ContentType, probe.ContentLength);
        }

        return OtherNonSuccessText(probe.StatusCode);
    }

    /// <summary>Compact read-only/copyable diagnostic summary available after a check.</summary>
    public static string SummaryText(DirectLinkProbe probe)
    {
        var lines = new System.Collections.Generic.List<string>();
        if (probe.RequestedUri is not null)
        {
            lines.Add("Requested URL: " + probe.RequestedUri.AbsoluteUri);
        }
        if (probe.FinalUri is not null
            && probe.RequestedUri is not null
            && probe.FinalUri.AbsoluteUri != probe.RequestedUri.AbsoluteUri)
        {
            lines.Add("Final URL: " + probe.FinalUri.AbsoluteUri);
        }
        if (probe.GotResponse)
        {
            lines.Add("HTTP status: " + probe.StatusCodeNumber.ToString());
        }
        if (!string.IsNullOrWhiteSpace(probe.ContentType))
        {
            lines.Add("Content type: " + probe.ContentType);
        }
        if (probe.ContentLength is not null)
        {
            lines.Add("Content length: " + probe.ContentLength.ToString());
        }
        if (!string.IsNullOrWhiteSpace(probe.ContentDisposition))
        {
            lines.Add("Content-Disposition: " + probe.ContentDisposition);
        }
        return string.Join("\n", lines);
    }
}
