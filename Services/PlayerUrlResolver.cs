using System;

namespace IArchiveMovieBrowser.Services;

/// <summary>Outcome of resolving a canonical Internet Archive video URL for external playback.</summary>
public enum PlayerResolutionOutcome
{
    /// <summary>A final 2xx response with a trusted HTTPS *.archive.org final URI was obtained.</summary>
    Resolved,

    /// <summary>The caller's token requested cancellation before a decision.</summary>
    Cancelled,

    /// <summary>The server refused access (401/403).</summary>
    Restricted,

    /// <summary>The server returned 404.</summary>
    Missing,

    /// <summary>An otherwise non-success HTTP response was received.</summary>
    NonSuccessHttp,

    /// <summary>The final URI (if any) is not a trusted, launchable location.</summary>
    UnsafeFinalUri,

    /// <summary>The request/TLS/network failed; no usable decision was reached.</summary>
    NetworkFailure
}

/// <summary>Structured result of resolving a canonical video URL to a final delivery URI.</summary>
public sealed class PlayerResolutionResult
{
    public PlayerResolutionOutcome Outcome { get; }
    public Uri? FinalUri { get; }
    public System.Net.HttpStatusCode? StatusCode { get; }

    public bool IsResolved => Outcome == PlayerResolutionOutcome.Resolved;

    private PlayerResolutionResult(
        PlayerResolutionOutcome outcome,
        Uri? finalUri,
        System.Net.HttpStatusCode? statusCode)
    {
        Outcome = outcome;
        FinalUri = finalUri;
        StatusCode = statusCode;
    }

    public static PlayerResolutionResult Resolved(Uri finalUri) =>
        new PlayerResolutionResult(PlayerResolutionOutcome.Resolved, finalUri, null);

    public static PlayerResolutionResult Cancelled() =>
        new PlayerResolutionResult(PlayerResolutionOutcome.Cancelled, null, null);

    public static PlayerResolutionResult Restricted() =>
        new PlayerResolutionResult(PlayerResolutionOutcome.Restricted, null, System.Net.HttpStatusCode.Unauthorized);

    public static PlayerResolutionResult Missing() =>
        new PlayerResolutionResult(PlayerResolutionOutcome.Missing, null, System.Net.HttpStatusCode.NotFound);

    public static PlayerResolutionResult NonSuccess(System.Net.HttpStatusCode status) =>
        new PlayerResolutionResult(PlayerResolutionOutcome.NonSuccessHttp, null, status);

    public static PlayerResolutionResult UnsafeFinal(Uri? finalUri) =>
        new PlayerResolutionResult(PlayerResolutionOutcome.UnsafeFinalUri, finalUri, null);

    public static PlayerResolutionResult NetworkFailure() =>
        new PlayerResolutionResult(PlayerResolutionOutcome.NetworkFailure, null, null);
}

/// <summary>
/// Pure final-URI trust policy and player-launch-request construction for the selected-video
/// resolve-then-open flow. No networking or process launching happens here.
/// </summary>
public static class PlayerUrlResolver
{
    /// <summary>
    /// True only when a final resolved URI is safe to hand to the external player: absolute,
    /// HTTPS, host exactly <c>archive.org</c> or a subdomain ending in <c>.archive.org</c>,
    /// no user-info, and no unexpected non-default port.
    /// </summary>
    public static bool IsTrustedFinalUri(Uri? finalUri)
    {
        if (finalUri is null)
        {
            return false;
        }
        if (finalUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(finalUri.Host))
        {
            return false;
        }
        string host = finalUri.Host.ToLower();
        if (host != "archive.org" && !host.EndsWith(".archive.org"))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(finalUri.UserInfo))
        {
            return false;
        }
        // Reject any explicit non-default port (HTTPS default is 443; absent/0 is "no explicit port").
        if (finalUri.Port > 0 && finalUri.Port != 443)
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// Pure bridge between a resolution result and the external-player launch request. Produces a
/// launch request only for a resolved, trusted final URI; returns null for every other outcome so
/// no external process is started.
/// </summary>
public static class PlayerResolveThenLaunchLogic
{
    public static ExternalPlayerLaunchRequest? BuildLaunchRequest(
        PlayerResolutionResult result,
        string validatedExecutablePath)
    {
        if (result is null || result.Outcome != PlayerResolutionOutcome.Resolved)
        {
            return null;
        }
        if (!PlayerUrlResolver.IsTrustedFinalUri(result.FinalUri) || result.FinalUri is null)
        {
            return null;
        }
        return new ExternalPlayerLaunchRequest(
            validatedExecutablePath,
            result.FinalUri.AbsoluteUri);
    }
}

/// <summary>Exact user-visible status text and the outcome-to-text mapping for player resolution.</summary>
public static class PlayerResolveDisplayText
{
    /// <summary>Shown while the canonical URL is being resolved through redirects.</summary>
    public const string ResolvingText = "Resolving selected video link…";

    /// <summary>HTTP 401/403 (access restricted).</summary>
    public const string RestrictedText =
        "Internet Archive did not make this file available for playback.";

    /// <summary>HTTP 404 (file not found).</summary>
    public const string NotFoundText =
        "Internet Archive could not find this selected file.";

    /// <summary>Final location fell outside the trusted IA delivery policy.</summary>
    public const string UnsafeFinalText =
        "The video link resolved to an unexpected location and was not opened.";

    /// <summary>Transport/TLS/request failure.</summary>
    public const string NetworkFailureText =
        "Could not open the selected video. Check your connection and try again.";

    /// <summary>User-visible cancellation (only for an intentional user-cancel path).</summary>
    public const string CancelledText = "Opening selected video was cancelled.";

    /// <summary>Other non-success HTTP status wording.</summary>
    public static string OtherNonSuccessText(System.Net.HttpStatusCode status) =>
        "Internet Archive returned HTTP " + ((int)status).ToString() + " for this selected file.";

    /// <summary>Maps a resolution result to a single user-visible Details status line.</summary>
    public static string OutcomeText(PlayerResolutionResult result)
    {
        switch (result.Outcome)
        {
            case PlayerResolutionOutcome.Cancelled:
                return CancelledText;
            case PlayerResolutionOutcome.Restricted:
                return RestrictedText;
            case PlayerResolutionOutcome.Missing:
                return NotFoundText;
            case PlayerResolutionOutcome.UnsafeFinalUri:
                return UnsafeFinalText;
            case PlayerResolutionOutcome.NetworkFailure:
                return NetworkFailureText;
            case PlayerResolutionOutcome.Resolved:
                return ExternalPlayerLaunchDisplayText.OpeningStatus;
            case PlayerResolutionOutcome.NonSuccessHttp:
            default:
                return OtherNonSuccessText(
                    result.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
        }
    }
}