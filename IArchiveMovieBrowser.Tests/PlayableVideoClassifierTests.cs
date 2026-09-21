using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class PlayableVideoClassifierTests
{
    private static InternetArchiveRemoteFile File(string? name, string? format = "MPEG4") =>
        new InternetArchiveRemoteFile(name!, format, "original", 1234, 0, "abc");

    private static IReadOnlyList<InternetArchiveRemoteFile> Select(params string[] names)
    {
        var list = new List<InternetArchiveRemoteFile>();
        foreach (string n in names)
        {
            list.Add(File(n));
        }
        return PlayableVideoClassifier.SelectPlayableCandidates(list);
    }

    [Theory]
    [InlineData("movie.mp4")]
    [InlineData("movie.m4v")]
    [InlineData("movie.mkv")]
    [InlineData("movie.avi")]
    [InlineData("movie.mov")]
    [InlineData("movie.wmv")]
    [InlineData("movie.webm")]
    [InlineData("movie.mpg")]
    [InlineData("movie.mpeg")]
    [InlineData("movie.m2ts")]
    [InlineData("movie.ts")]
    [InlineData("movie.ogv")]
    [InlineData("movie.3gp")]
    public void SupportedExtensions_AreCandidates(string name)
    {
        Assert.Single(Select(name));
        Assert.Equal(name, Select(name)[0].Name);
    }

    [Theory]
    [InlineData("movie.MP4")]
    [InlineData("movie.Mkv")]
    [InlineData("movie.WEBM")]
    [InlineData("MOVIE.AVI")]
    public void MixedCaseExtensions_AreCandidates(string name)
    {
        Assert.Single(Select(name));
    }

    [Theory]
    [InlineData("poster.jpg")]
    [InlineData("thumb.png")]
    [InlineData("sub.srt")]
    [InlineData("meta.xml")]
    [InlineData("data.json")]
    [InlineData("notes.txt")]
    [InlineData("file.zip")]
    [InlineData("release.torrent")]
    public void NonVideoExtensions_AreRejected(string name)
    {
        Assert.Empty(Select(name));
    }

    [Fact]
    public void BlankAndExtensionless_AreRejected()
    {
        Assert.Empty(Select(""));
        Assert.Empty(Select("moviefile"));
        Assert.Empty(Select("file."));
        Assert.Empty(Select("   "));
    }

    [Fact]
    public void NullAndMalformedEntries_AreTolerated()
    {
        var list = new List<InternetArchiveRemoteFile>
        {
            null!,
            File(null!),
            File(""),
            File("    "),
            File("real.mp4")
        };
        var result = PlayableVideoClassifier.SelectPlayableCandidates(list);

        Assert.Single(result);
        Assert.Equal("real.mp4", result[0].Name);
    }

    [Fact]
    public void Duplicates_AreDeduplicatedByCaseInsensitiveName()
    {
        var list = new List<InternetArchiveRemoteFile>
        {
            File("Movie.mp4"),
            File("movie.MP4"),
            File("movie2.mp4"),
            File("movie2.mp4")
        };
        var result = PlayableVideoClassifier.SelectPlayableCandidates(list);

        Assert.Equal(2, result.Count);
        Assert.Equal("Movie.mp4", result[0].Name);
        Assert.Equal("movie2.mp4", result[1].Name);
    }

    [Fact]
    public void SourceOrder_IsPreserved()
    {
        var result = Select("b.mp4", "a.ts", "c.mkv", "z.mp4");
        Assert.Equal(new[] { "b.mp4", "a.ts", "c.mkv", "z.mp4" }, Names(result));
    }

    [Fact]
    public void NullOrEmptyInput_ReturnsEmpty()
    {
        Assert.Empty(PlayableVideoClassifier.SelectPlayableCandidates(null));
        Assert.Empty(PlayableVideoClassifier.SelectPlayableCandidates(new List<InternetArchiveRemoteFile>()));
    }

    [Fact]
    public void DisplayStrings_AreExact()
    {
        Assert.Equal("Playable video files", DetailsDisplayText.PlayableVideoHeading());
        Assert.Equal(
            "No likely playable video files were identified.",
            DetailsDisplayText.NoPlayableVideoMessage());
    }

    [Fact]
    public void NoCandidates_DisplaysEmptyMessageViaHelper()
    {
        // The classifier returns an empty list; the display message is surfaced later.
        Assert.Empty(PlayableVideoClassifier.SelectPlayableCandidates(Select("poster.jpg")));
        Assert.Equal(
            "No likely playable video files were identified.",
            DetailsDisplayText.NoPlayableVideoMessage());
    }

    private static string[] Names(IReadOnlyList<InternetArchiveRemoteFile> files)
    {
        var result = new string[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            result[i] = files[i].Name;
        }
        return result;
    }
}
