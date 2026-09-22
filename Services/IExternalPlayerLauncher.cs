namespace IArchiveMovieBrowser.Services;

/// <summary>Result of an attempted external-player launch request.</summary>
public enum ExternalPlayerLaunchOutcome
{
    /// <summary>The process-start request was accepted (no claim that playback/stream succeeded).</summary>
    Succeeded,

    /// <summary>The process could not be started; see <see cref="ExternalPlayerLaunchResult.FailureReason"/>.</summary>
    Failed
}

/// <summary>
/// Outcome of an <see cref="IExternalPlayerLauncher.Launch"/> call. Success only means the
/// launch request was made — the stream is not probed or verified.
/// </summary>
public sealed class ExternalPlayerLaunchResult
{
    public ExternalPlayerLaunchOutcome Outcome { get; }
    public string? FailureReason { get; }

    public bool Succeeded => Outcome == ExternalPlayerLaunchOutcome.Succeeded;

    public ExternalPlayerLaunchResult(ExternalPlayerLaunchOutcome outcome, string? failureReason)
    {
        Outcome = outcome;
        FailureReason = failureReason;
    }
}

/// <summary>
/// Isolated external-player process-start boundary. UI code never calls
/// <see cref="System.Diagnostics.Process"/> directly; it goes through this abstraction so unit
/// tests can verify request construction and result handling without launching a program.
/// </summary>
public interface IExternalPlayerLauncher
{
    /// <summary>Starts the configured executable with the single direct URL argument.</summary>
    ExternalPlayerLaunchResult Launch(ExternalPlayerLaunchRequest request);
}