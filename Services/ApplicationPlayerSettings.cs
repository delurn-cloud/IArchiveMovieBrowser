using System.Configuration;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Default <see cref="IPlayerExecutableStorage"/> backed by the standard .NET user-scoped
/// application settings (a per-user <c>user.config</c> under the user's application-data
/// folder). Satisfies the "standard WPF/.NET user-scoped setting mechanism" requirement and
/// writes nothing into the repository, app directory, registry, or machine-wide config.
/// </summary>
public sealed class ApplicationPlayerSettings : ApplicationSettingsBase, IPlayerExecutableStorage
{
    private const string PlayerExecutableKey = "PlayerExecutablePath";

    [UserScopedSetting]
    [DefaultSettingValue("")]
    public string PlayerExecutablePath
    {
        get { return (string)this[PlayerExecutableKey]; }
        set { this[PlayerExecutableKey] = value; }
    }

    public string? Load()
    {
        string value = PlayerExecutablePath;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void Save(string? path)
    {
        PlayerExecutablePath = path ?? "";
        Save();
    }

    public void Clear()
    {
        PlayerExecutablePath = "";
        Save();
    }
}