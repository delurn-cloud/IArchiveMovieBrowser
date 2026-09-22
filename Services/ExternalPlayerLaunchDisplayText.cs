namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Exact, fixed UI labels and status text for the "Open in external player" action in the Details
/// window. Kept pure so the presentation strings can be unit-tested.
/// </summary>
public static class ExternalPlayerLaunchDisplayText
{
    public const string ButtonLabel = "Open selected video in external player";

    /// <summary>Shown when the button is enabled: what clicking it will do.</summary>
    public const string ReadyHint =
        "Will open the selected direct stream using the configured external player.";

    /// <summary>Shown when no playable candidate is selected.</summary>
    public const string NoSelectionExplanation =
        "Select a playable video file to open it in the external player.";

    /// <summary>Shown when the external player is not configured or its path is missing/invalid.</summary>
    public const string ConfigurePlayerExplanation =
        "Configure a valid external player in the main window to open selected videos.";

    /// <summary>Shown when the selected candidate cannot be resolved to a direct stream URL.</summary>
    public const string NoDirectUrlExplanation =
        "The selected video could not be resolved to a direct stream URL.";

    /// <summary>Status shown after a successful launch request (kept modest: no playback claim).</summary>
    public const string OpeningStatus =
        "Opening selected video in external player.";

    /// <summary>Falls back to when the launcher reports a failure without a usable reason.</summary>
    public const string PlayerUnavailableStatus =
        "Could not start the external player. Check that the configured executable is still available, then try again.";

    /// <summary>Maps a not-ready reason to its user-facing explanation.</summary>
    public static string NotReadyExplanation(ExternalPlayerLaunchNotReadyReason? reason)
    {
        return reason switch
        {
            ExternalPlayerLaunchNotReadyReason.ConfigurePlayer => ConfigurePlayerExplanation,
            ExternalPlayerLaunchNotReadyReason.NoDirectUrl => NoDirectUrlExplanation,
            _ => NoSelectionExplanation
        };
    }

    /// <summary>Status to show after a failed launch request using the launcher failure reason.</summary>
    public static string FailureStatus(string? failureReason)
    {
        return string.IsNullOrWhiteSpace(failureReason)
            ? PlayerUnavailableStatus
            : "Could not open in external player. " + failureReason;
    }
}