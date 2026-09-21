using System.Collections.Generic;

namespace IArchiveMovieBrowser.Domain;

/// <summary>One document from an Internet Archive advancedsearch result set.</summary>
public sealed class InternetArchiveSearchResult
{
    /// <summary>Internet Archive item identifier; always present and nonblank.</summary>
    public string Identifier { get; }

    public string? Title { get; }
    public IReadOnlyList<string>? Creators { get; }
    public string? Date { get; }
    public string? Year { get; }
    public string? MediaType { get; }
    public IReadOnlyList<string>? Collections { get; }
    public long? Downloads { get; }

    public InternetArchiveSearchResult(
        string identifier,
        string? title,
        IReadOnlyList<string>? creators,
        string? date,
        string? year,
        string? mediaType,
        IReadOnlyList<string>? collections,
        long? downloads)
    {
        Identifier = identifier;
        Title = title;
        Creators = creators;
        Date = date;
        Year = year;
        MediaType = mediaType;
        Collections = collections;
        Downloads = downloads;
    }
}