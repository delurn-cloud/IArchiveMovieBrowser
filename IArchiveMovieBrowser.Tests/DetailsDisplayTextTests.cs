using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class DetailsDisplayTextTests
{
    private static InternetArchiveItemMetadata Metadata(
        string? title,
        string? date,
        string? year,
        string? mediaType,
        string? licenseUrl,
        IReadOnlyList<InternetArchiveRemoteFile>? files)
    {
        return new InternetArchiveItemMetadata(
            "item-1",
            title,
            "desc",
            new List<string> { "Anne" },
            date,
            year,
            mediaType,
            new List<string> { "colA" },
            new List<string> { "sub1" },
            licenseUrl,
            files ?? new List<InternetArchiveRemoteFile>());
    }

    private static InternetArchiveRemoteFile File(string name, long? size) =>
        new InternetArchiveRemoteFile(name, "MPEG4", "original", size, null, "abc");

    [Fact]
    public void Title_FallsBackWhenMissing()
    {
        Assert.Equal("(not supplied)", DetailsDisplayText.TitleText(Metadata(null, null, null, null, null, null)));
        Assert.Equal("My Film", DetailsDisplayText.TitleText(Metadata("My Film", null, null, "movies", null, null)));
    }

    [Fact]
    public void DateYear_PrefersDateThenYearThenFallback()
    {
        Assert.Equal("2021-05-01", DetailsDisplayText.DateYearText(Metadata("T", "2021-05-01", "2021", null, null, null)));
        Assert.Equal("1999", DetailsDisplayText.DateYearText(Metadata("T", null, "1999", null, null, null)));
        Assert.Equal("(not supplied)", DetailsDisplayText.DateYearText(Metadata("T", null, null, null, null, null)));
    }

    [Fact]
    public void MediaType_FallsBack()
    {
        Assert.Equal("movies", DetailsDisplayText.MediaTypeText(Metadata("T", null, null, "movies", null, null)));
        Assert.Equal("(unknown type)", DetailsDisplayText.MediaTypeText(Metadata("T", null, null, null, null, null)));
    }

    [Fact]
    public void JoinedText_JoinsAndFallsBack()
    {
        Assert.Equal("A, B", DetailsDisplayText.JoinedText(new List<string> { "A", "B" }));
        Assert.Equal("(not supplied)", DetailsDisplayText.JoinedText(null));
        Assert.Equal("(not supplied)", DetailsDisplayText.JoinedText(new List<string>()));
    }

    [Fact]
    public void License_HasLabelAndUrlOrFallback()
    {
        Assert.Equal(
            "View license — https://lic",
            DetailsDisplayText.LicenseText(Metadata("T", null, null, null, "https://lic", null)));
        Assert.Equal("(not supplied)", DetailsDisplayText.LicenseText(Metadata("T", null, null, null, null, null)));
    }

    [Theory]
    [InlineData("video.mp4", "Video", true)]
    [InlineData("song.mp3", "Audio", true)]
    [InlineData("notes.txt", "(other)", false)]
    [InlineData("big_buck_bunny.MKV", "Video", true)]
    public void KindAndMediaCandidate_CallsClassificationCore(string name, string kindText, bool candidate)
    {
        var file = File(name, 1000L);

        Assert.Equal(kindText, DetailsDisplayText.KindText(file));
        Assert.Equal(candidate ? "Yes" : "No", DetailsDisplayText.MediaCandidateText(file));
        Assert.Equal(candidate, DetailsDisplayText.IsMediaCandidate(file));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(4096, "4 KB")]
    [InlineData(5L * 1024L * 1024L, "5 MB")]
    [InlineData(2L * 1024L * 1024L * 1024L, "2 GB")]
    public void Size_FormatsWithFallback(long bytes, string expected)
    {
        Assert.Equal(expected, DetailsDisplayText.SizeText(bytes));
        Assert.Equal("(unknown size)", DetailsDisplayText.SizeText(null));
    }

    [Fact]
    public void AnyMediaCandidate_DetectsVideoAudio()
    {
        Assert.True(DetailsDisplayText.AnyMediaCandidate(new List<InternetArchiveRemoteFile> { File("a.mp4", null), File("b.txt", null) }));
        Assert.False(DetailsDisplayText.AnyMediaCandidate(new List<InternetArchiveRemoteFile> { File("b.txt", null), File("c.json", null) }));
        Assert.False(DetailsDisplayText.AnyMediaCandidate(new List<InternetArchiveRemoteFile>()));
    }

    [Fact]
    public void Status_TextsAreExact()
    {
        Assert.Equal("Loading details…", DetailsDisplayText.StatusLoadingDetails());
        Assert.Equal("Ready", DetailsDisplayText.StatusReadyDetails());
        Assert.Equal("No usable media files found.", DetailsDisplayText.StatusNoUsableMedia());
        Assert.Equal("Error: network down", DetailsDisplayText.StatusError("network down"));
        Assert.Equal("Cancelled", DetailsDisplayText.StatusCancelled());
    }
}