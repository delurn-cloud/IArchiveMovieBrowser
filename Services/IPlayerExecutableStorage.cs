namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Persists exactly one user-configured external media-player executable path, scoped to the
/// current user. Implementations must store only a validated full absolute path and must not
/// write into the repository, the application directory, the registry, environment
/// variables, or machine-wide configuration.
/// </summary>
public interface IPlayerExecutableStorage
{
    /// <summary>Returns the saved path, or null when none is configured.</summary>
    string? Load();

    /// <summary>Saves the given full absolute .exe path (or null/blank to clear).</summary>
    void Save(string? path);

    /// <summary>Removes the saved configuration.</summary>
    void Clear();
}