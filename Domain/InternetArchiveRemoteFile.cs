using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// One file entry from an Internet Archive item's metadata file inventory.
/// </summary>
public sealed class InternetArchiveRemoteFile
{
    /// <summary>File name; always present and nonblank.</summary>
    public string Name { get; }

    public string? Format { get; }
    public string? Source { get; }
    public long? Size { get; }
    public long? MTime { get; }
    public string? Sha1 { get; }

    public InternetArchiveRemoteFile(
        string name,
        string? format,
        string? source,
        long? size,
        long? mtime,
        string? sha1)
    {
        Name = name;
        Format = format;
        Source = source;
        Size = size;
        MTime = mtime;
        Sha1 = sha1;
    }
}