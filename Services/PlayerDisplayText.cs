using System.IO;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Exact, fixed UI labels and concise status text for the External player configuration
/// area. Kept pure so the presentation strings can be unit-tested.
/// </summary>
public static class PlayerDisplayText
{
    public const string AreaLabel = "External player";
    public const string ChooseButtonLabel = "Choose player…";
    public const string ChangeButtonLabel = "Change…";
    public const string ClearButtonLabel = "Clear";

    public const string NotConfiguredStatus =
        "Select a media-player executable (.exe) to open selected video streams.";

    public const string MissingStatus =
        "Configured player file is missing or unavailable. Choose it again or clear it.";

    public const string InvalidStatus =
        "The configured player path is not a valid .exe executable.";

    public static string ReadyStatus(string validatedPath)
    {
        string name = Path.GetFileName(validatedPath);
        return "External player ready: " + name;
    }
}