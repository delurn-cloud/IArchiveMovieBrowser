using System;
using System.Diagnostics;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Builds the <see cref="ProcessStartInfo"/> for launching the configured external media player
/// with a single discrete direct-URL argument. Extracted so the exact process-start request
/// (FileName source, UseShellExecute=false, ArgumentList usage) can be unit-tested without
/// launching any process.
/// </summary>
public static class ExternalPlayerProcessStartInfoFactory
{
    /// <summary>
    /// Returns a ProcessStartInfo that directly invokes <see cref="ExternalPlayerLaunchRequest.ExecutablePath"/>
    /// with no shell/file-association, passing the direct URL as exactly one discrete argument.
    /// </summary>
    public static ProcessStartInfo Build(ExternalPlayerLaunchRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(request.Url);
        return startInfo;
    }
}

/// <summary>
/// Production <see cref="IExternalPlayerLauncher"/> that starts the configured executable
/// directly with the canonical direct URL passed as a single argument via
/// <c>ProcessStartInfo.ArgumentList</c>. No cmd.exe/PowerShell, no shell command string, no
/// file association, and <c>UseShellExecute=false</c>. Only the process start is wrapped in the
/// exception handling; the stream itself is neither probed nor verified.
/// </summary>
public sealed class ExternalPlayerLauncher : IExternalPlayerLauncher
{
    public ExternalPlayerLaunchResult Launch(ExternalPlayerLaunchRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            ProcessStartInfo startInfo = ExternalPlayerProcessStartInfoFactory.Build(request);
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return new ExternalPlayerLaunchResult(
                    ExternalPlayerLaunchOutcome.Failed,
                    "The external player could not be started.");
            }

            return new ExternalPlayerLaunchResult(ExternalPlayerLaunchOutcome.Succeeded, null);
        }
        catch (Exception ex)
        {
            return new ExternalPlayerLaunchResult(
                ExternalPlayerLaunchOutcome.Failed,
                "Could not start the external player: " + ex.Message);
        }
    }
}