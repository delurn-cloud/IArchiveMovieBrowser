using System;
using System.Diagnostics;
using System.IO;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class ExternalPlayerLaunchTests : IDisposable
{
    private readonly string _tempExe;

    public ExternalPlayerLaunchTests()
    {
        _tempExe = Path.Combine(Path.GetTempPath(), "iamb_launch_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(_tempExe, "fixture");
    }

    public void Dispose()
    {
        try { File.Delete(_tempExe); } catch { /* best effort */ }
    }

    // --- Readiness: no selection ----------------------------------------------

    [Fact]
    public void NoSelectedCandidate_NotReady_NoSelectionReason()
    {
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness("item", null, null);
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchState.NotReady, readiness.State);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.NoSelection, readiness.NotReadyReason);
        Assert.Null(readiness.Request);
    }

    [Fact]
    public void BlankOrInvalidSelectedFilename_NotReady()
    {
        Assert.False(ExternalPlayerLaunchLogic.BuildReadiness("item", "", _tempExe).IsReady);
        Assert.False(ExternalPlayerLaunchLogic.BuildReadiness("item", "   ", _tempExe).IsReady);
    }

    // --- Readiness: player configuration --------------------------------------

    [Fact]
    public void ValidSelectionButNoConfiguredPlayer_NotReady_ConfigurePlayerReason()
    {
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness("item", "clip.mp4", null);
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.ConfigurePlayer, readiness.NotReadyReason);
    }

    [Fact]
    public void MissingConfiguredPlayerPath_NotReady()
    {
        string missing = Path.Combine(Path.GetTempPath(), "gone_" + Guid.NewGuid().ToString("N") + ".exe");
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness("item", "clip.mp4", missing);
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.ConfigurePlayer, readiness.NotReadyReason);
    }

    [Fact]
    public void InvalidConfiguredPlayerPath_NotReady()
    {
        // Non-absolute, non-exe value is Invalid.
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness("item", "clip.mp4", "player.exe");
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.ConfigurePlayer, readiness.NotReadyReason);
    }

    // --- Readiness: direct URL resolvability -----------------------------------

    [Fact]
    public void BlankItemIdentifier_NotReady_NoDirectUrlReason()
    {
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness(null, "clip.mp4", _tempExe);
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.NoDirectUrl, readiness.NotReadyReason);
    }

    [Fact]
    public void InvalidItemIdentifier_NotReady_NoDirectUrlReason()
    {
        var readiness = ExternalPlayerLaunchLogic.BuildReadiness("a/b", "clip.mp4", _tempExe);
        Assert.False(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchNotReadyReason.NoDirectUrl, readiness.NotReadyReason);
    }

    // --- Readiness: valid selection + valid player ------------------------------

    [Fact]
    public void ValidSelectionAndValidPlayer_ReadyWithExactPathAndCanonicalUrl()
    {
        const string identifier = "item-123";
        const string filename = "film.mp4";

        var readiness = ExternalPlayerLaunchLogic.BuildReadiness(identifier, filename, _tempExe);

        Assert.True(readiness.IsReady);
        Assert.Equal(ExternalPlayerLaunchState.Ready, readiness.State);
        Assert.Null(readiness.NotReadyReason);
        Assert.NotNull(readiness.Request);
        Assert.Equal(_tempExe, readiness.Request!.ExecutablePath);

        string expectedUrl = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename).ToString();
        Assert.Equal(expectedUrl, readiness.Request.Url);
    }

    // --- Request construction ---------------------------------------------------

    [Fact]
    public void BuildStartInfo_FileNameIsConfiguredExecutable_NoShell()
    {
        var request = new ExternalPlayerLaunchRequest(_tempExe, "https://archive.org/download/i/f.mp4");
        ProcessStartInfo psi = ExternalPlayerProcessStartInfoFactory.Build(request);

        Assert.Equal(_tempExe, psi.FileName);
        Assert.False(psi.UseShellExecute);
        // Not routed through a shell/program association.
        string lowerName = Path.GetFileName(psi.FileName).ToLower();
        Assert.NotEqual("cmd.exe", lowerName);
        Assert.NotEqual("powershell.exe", lowerName);
        Assert.NotEqual("pwsh.exe", lowerName);
    }

    [Fact]
    public void BuildStartInfo_UrlIsOneDiscreteArgument_NotConcatenatedWithExecutable()
    {
        const string url = "https://archive.org/download/my item/video (cut).mp4";
        var request = new ExternalPlayerLaunchRequest(_tempExe, url);
        ProcessStartInfo psi = ExternalPlayerProcessStartInfoFactory.Build(request);

        Assert.Single(psi.ArgumentList);
        Assert.Equal(url, psi.ArgumentList[0]);
        Assert.DoesNotContain(_tempExe, psi.ArgumentList[0]);
    }
// --- Result status mapping ---------------------------------------------------

    [Fact]
    public void FailedLaunchResult_MapsToActionableStatus_NotSuccess()
    {
        var result = new ExternalPlayerLaunchResult(
            ExternalPlayerLaunchOutcome.Failed,
            "Could not start the external player: boom");

        Assert.False(result.Succeeded);
        Assert.False(result.Outcome == ExternalPlayerLaunchOutcome.Succeeded);
        // The surfaced status is actionable and never claims the video is playing.
        string status = ExternalPlayerLaunchDisplayText.FailureStatus(result.FailureReason);
        Assert.Contains("Could not open in external player", status);
        Assert.DoesNotContain("is playing", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedLaunchWithoutReason_FallsBackToSafeStatus()
    {
        var result = new ExternalPlayerLaunchResult(ExternalPlayerLaunchOutcome.Failed, null);
        string status = ExternalPlayerLaunchDisplayText.FailureStatus(result.FailureReason);
        Assert.Equal(ExternalPlayerLaunchDisplayText.PlayerUnavailableStatus, status);
        Assert.DoesNotContain("success", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessfulLaunchResult_ProducesOpeningStatus_NoPlaybackClaim()
    {
        var result = new ExternalPlayerLaunchResult(ExternalPlayerLaunchOutcome.Succeeded, null);
        Assert.True(result.Succeeded);
        Assert.Equal(
            "Opening selected video in external player.",
            ExternalPlayerLaunchDisplayText.OpeningStatus);
        // Modest claim: only that the launch was requested, not that playback is verified.
        Assert.DoesNotContain("playing", ExternalPlayerLaunchDisplayText.OpeningStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verified", ExternalPlayerLaunchDisplayText.OpeningStatus, StringComparison.OrdinalIgnoreCase);
    }

    // --- Display text constants ---------------------------------------------------

    [Fact]
    public void DisplayText_ExactStrings()
    {
        Assert.Equal("Open selected video in external player", ExternalPlayerLaunchDisplayText.ButtonLabel);
        Assert.Equal(
            "Select a playable video file to open it in the external player.",
            ExternalPlayerLaunchDisplayText.NoSelectionExplanation);
        Assert.Equal(
            "Configure a valid external player in the main window to open selected videos.",
            ExternalPlayerLaunchDisplayText.ConfigurePlayerExplanation);
        Assert.Equal(
            "Will open the selected direct stream using the configured external player.",
            ExternalPlayerLaunchDisplayText.ReadyHint);
    }
}