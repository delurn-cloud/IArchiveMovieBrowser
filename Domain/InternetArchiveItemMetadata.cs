using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>
/// The current identifying metadata and file inventory of a single Internet Archive item,
/// as returned by the /metadata/{identifier} endpoint at fetch time.
/// </summary>
public sealed class InternetArchiveItemMetadata
{
    public string Identifier { get; }
    public string? Title { get; }
    public string? Description { get; }
    public IReadOnlyList<string>? Creators { get; }
    public string? Date { get; }
    public string? Year { get; }
    public string? MediaType { get; }
    public IReadOnlyList<string>? Collections { get; }
    public IReadOnlyList<string>? Subjects { get; }
    public string? LicenseUrl { get; }
    public IReadOnlyList<InternetArchiveRemoteFile> Files { get; }

    public InternetArchiveItemMetadata(
        string identifier,
        string? title,
        string? description,
        IReadOnlyList<string>? creators,
        string? date,
        string? year,
        string? mediaType,
        IReadOnlyList<string>? collections,
        IReadOnlyList<string>? subjects,
        string? licenseUrl,
        IReadOnlyList<InternetArchiveRemoteFile> files)
    {
        Identifier = identifier;
        Title = title;
        Description = description;
        Creators = creators;
        Date = date;
        Year = year;
        MediaType = mediaType;
        Collections = collections;
        Subjects = subjects;
        LicenseUrl = licenseUrl;
        Files = files ?? new List<InternetArchiveRemoteFile>();
    }
}