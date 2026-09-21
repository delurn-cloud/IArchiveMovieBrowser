using System;
using System.IO;

namespace IArchiveMovieBrowser.Services;

/// <summary>User-visible state of the configured external media-player executable.</summary>
public enum PlayerConfigurationState
{
    /// <summary>No executable path configured.</summary>
    NotConfigured,

    /// <summary>A valid, existing absolute .exe path is configured and usable.</summary>
    Ready,

    /// <summary>A previous absolute .exe path is configured but the file no longer exists.</summary>
    Missing,

    /// <summary>The stored path is not a usable absolute .exe file path (syntax/layout error).</summary>
    Invalid
}

/// <summary>Result of analyzing the configured external-player executable for the UI.</summary>
public sealed class PlayerConfiguration
{
    public PlayerConfigurationState State { get; }
    public string? ValidatedPath { get; }

    public PlayerConfiguration(PlayerConfigurationState state, string? validatedPath)
    {
        State = state;
        ValidatedPath = validatedPath;
    }
}

/// <summary>
/// Pure validation/state logic for the single user-configured external media-player
/// executable. It never launches, inspects, or probes the file; it only validates that the
/// stored value is a usable absolute path to an existing .exe.
/// </summary>
public static class ExternalPlayerLogic
{
    /// <summary>
    /// Validates a candidate path for a media-player executable (no file-system mutation).
    /// Returns true when the path is an absolute path to an existing .exe file and the same
    /// normalized full path is emitted in <paramref name="normalizedFullPath"/>.
    /// </summary>
    public static bool TryNormalizeForSave(string? path, out string? normalizedFullPath)
    {
        normalizedFullPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string trimmed = path.Trim();
        if (!Path.IsPathRooted(trimmed))
        {
            return false; // must be an absolute full path
        }

        if (!string.Equals(Path.GetExtension(trimmed), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false; // must be an .exe, case-insensitively
        }

        if (Directory.Exists(trimmed))
        {
            return false; // must be a file, not a directory
        }

        if (!File.Exists(trimmed))
        {
            return false; // must exist as a file
        }

        // The normalized path is the full absolute path without trailing separator
        // artifacts.
        normalizedFullPath = trimmed;
        return true;
    }

    /// <summary>
    /// Builds the UI <see cref="PlayerConfiguration"/> for the stored saved path. A blank
    /// value means Not configured; a valid existing path means Ready; a syntactically valid
    /// but missing .exe means Missing; anything else is Invalid.
    /// </summary>
    public static PlayerConfiguration AnalyzeStoredPath(string? savedPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return new PlayerConfiguration(PlayerConfigurationState.NotConfigured, null);
        }

        string candidate = savedPath.Trim();

        // Syntax/layout checks that do not depend on existence.
        if (!Path.IsPathRooted(candidate)
            || !string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(candidate))
        {
            return new PlayerConfiguration(PlayerConfigurationState.Invalid, null);
        }

        if (!File.Exists(candidate))
        {
            return new PlayerConfiguration(PlayerConfigurationState.Missing, candidate);
        }

        return new PlayerConfiguration(PlayerConfigurationState.Ready, candidate);
    }
}