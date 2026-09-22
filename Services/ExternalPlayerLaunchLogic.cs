using System;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Whether the currently selected playable candidate is ready to be opened in the
/// configured external media player.
/// </summary>
public enum ExternalPlayerLaunchState
{
    /// <summary>A requirement is unmet (no selection, no valid player, no direct URL).</summary>
    NotReady,

    /// <summary>A <see cref="ExternalPlayerLaunchRequest"/> can be built and launched.</summary>
    Ready
}

/// <summary>
/// The specific unmet prerequisite when launch readiness is
/// <see cref="ExternalPlayerLaunchState.NotReady"/>. Drives the user-facing explanation.
/// </summary>
public enum ExternalPlayerLaunchNotReadyReason
{
    /// <summary>No playable candidate is currently selected.</summary>
    NoSelection,

    /// <summary>The external player is not configured, or its saved path is missing/invalid.</summary>
    ConfigurePlayer,

    /// <summary>The selected candidate could not be resolved to a direct stream URL
    /// (e.g. a blank/invalid item identifier).</summary>
    NoDirectUrl
}

/// <summary>
/// Immutable launch request handed to an <see cref="IExternalPlayerLauncher"/>. Contains the
/// exact configured executable full path and the canonical direct Internet Archive URL as a
/// single discrete value (never a concatenated command string).
/// </summary>
public sealed class ExternalPlayerLaunchRequest
{
    public string ExecutablePath { get; }
    public string Url { get; }

    public ExternalPlayerLaunchRequest(string executablePath, string url)
    {
        ExecutablePath = executablePath;
        Url = url;
    }
}

/// <summary>
/// Result of readiness analysis: either a ready request or a not-ready state plus the reason
/// that can be surfaced to the user.
/// </summary>
public sealed class ExternalPlayerLaunchReadiness
{
    public ExternalPlayerLaunchState State { get; }
    public ExternalPlayerLaunchNotReadyReason? NotReadyReason { get; }
    public ExternalPlayerLaunchRequest? Request { get; }

    public bool IsReady => State == ExternalPlayerLaunchState.Ready;

    private ExternalPlayerLaunchReadiness(
        ExternalPlayerLaunchState state,
        ExternalPlayerLaunchNotReadyReason? notReadyReason,
        ExternalPlayerLaunchRequest? request)
    {
        State = state;
        NotReadyReason = notReadyReason;
        Request = request;
    }

    internal static ExternalPlayerLaunchReadiness Ready(ExternalPlayerLaunchRequest request)
        => new ExternalPlayerLaunchReadiness(ExternalPlayerLaunchState.Ready, null, request);

    internal static ExternalPlayerLaunchReadiness NotReady(ExternalPlayerLaunchNotReadyReason reason)
        => new ExternalPlayerLaunchReadiness(ExternalPlayerLaunchState.NotReady, reason, null);
}

/// <summary>
/// Pure, WPF-independent launch-readiness logic. It never launches a process; it only decides
/// whether the current selection + configured player can yield a valid external-player request,
/// and builds that request using the existing canonical download-URL builder.
/// </summary>
public static class ExternalPlayerLaunchLogic
{
    /// <summary>
    /// Determines readiness for opening the given playable candidate in the configured external
    /// player. Returns a ready <see cref="ExternalPlayerLaunchRequest"/> containing the exact
    /// validated executable path and the canonical direct IA URL, or a not-ready state with a
    /// user-facing reason.
    /// </summary>
    public static ExternalPlayerLaunchReadiness BuildReadiness(
        string? identifier,
        string? filename,
        string? savedPlayerPath)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return ExternalPlayerLaunchReadiness.NotReady(
                ExternalPlayerLaunchNotReadyReason.NoSelection);
        }

        PlayerConfiguration player = ExternalPlayerLogic.AnalyzeStoredPath(savedPlayerPath);
        if (player.State != PlayerConfigurationState.Ready || player.ValidatedPath is null)
        {
            return ExternalPlayerLaunchReadiness.NotReady(
                ExternalPlayerLaunchNotReadyReason.ConfigurePlayer);
        }

        string url;
        try
        {
            url = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename).ToString();
        }
        catch (ArgumentException)
        {
            return ExternalPlayerLaunchReadiness.NotReady(
                ExternalPlayerLaunchNotReadyReason.NoDirectUrl);
        }
        catch (InvalidOperationException)
        {
            return ExternalPlayerLaunchReadiness.NotReady(
                ExternalPlayerLaunchNotReadyReason.NoDirectUrl);
        }

        return ExternalPlayerLaunchReadiness.Ready(
            new ExternalPlayerLaunchRequest(player.ValidatedPath, url));
    }
}