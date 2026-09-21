using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class InternetArchiveFileKindTests
{
    [Theory]
    [InlineData("movie.mp4", InternetArchiveFileKind.Video)]
    [InlineData("movie.MKV", InternetArchiveFileKind.Video)]
    [InlineData("clip.WebM", InternetArchiveFileKind.Video)]
    [InlineData("a.avi", InternetArchiveFileKind.Video)]
    [InlineData("a.mov", InternetArchiveFileKind.Video)]
    [InlineData("a.mpg", InternetArchiveFileKind.Video)]
    [InlineData("a.mpeg", InternetArchiveFileKind.Video)]
    [InlineData("a.m4v", InternetArchiveFileKind.Video)]
    [InlineData("a.ogv", InternetArchiveFileKind.Video)]

    [InlineData("song.mp3", InternetArchiveFileKind.Audio)]
    [InlineData("song.FLAC", InternetArchiveFileKind.Audio)]
    [InlineData("song.wav", InternetArchiveFileKind.Audio)]
    [InlineData("song.m4a", InternetArchiveFileKind.Audio)]
    [InlineData("song.ogg", InternetArchiveFileKind.Audio)]
    [InlineData("song.opus", InternetArchiveFileKind.Audio)]
    [InlineData("song.aac", InternetArchiveFileKind.Audio)]

    [InlineData("readme.txt", InternetArchiveFileKind.Other)]
    [InlineData("archive.zip", InternetArchiveFileKind.Other)]
    [InlineData("noext", InternetArchiveFileKind.Other)]
    public void ClassifyFileKind_CategorizesByExtension(string filename, InternetArchiveFileKind expected)
    {
        Assert.Equal(expected, InternetArchiveDownloadUrlBuilder.ClassifyFileKind(filename));
    }
}

public sealed class InternetArchiveFileDescriptorTests
{
    [Fact]
    public void CreateDescriptor_ExposesAllFieldsForVideo()
    {
        var descriptor = InternetArchiveDownloadUrlBuilder.CreateDescriptor("item-1", "film.mp4");

        Assert.Equal("item-1", descriptor.Identifier);
        Assert.Equal("film.mp4", descriptor.OriginalFilename);
        Assert.Equal(InternetArchiveFileKind.Video, descriptor.Kind);
        Assert.True(descriptor.IsMediaCandidate);
        Assert.Equal("https", descriptor.DownloadUri.Scheme);
        Assert.Equal("archive.org", descriptor.DownloadUri.Host);
    }

    [Fact]
    public void CreateDescriptor_AudioIsMediaCandidate()
    {
        var descriptor = InternetArchiveDownloadUrlBuilder.CreateDescriptor("item", "track.ogg");

        Assert.Equal(InternetArchiveFileKind.Audio, descriptor.Kind);
        Assert.True(descriptor.IsMediaCandidate);
    }

    [Fact]
    public void CreateDescriptor_OtherIsNotMediaCandidate()
    {
        var descriptor = InternetArchiveDownloadUrlBuilder.CreateDescriptor("item", "notes.txt");

        Assert.Equal(InternetArchiveFileKind.Other, descriptor.Kind);
        Assert.False(descriptor.IsMediaCandidate);
    }
}