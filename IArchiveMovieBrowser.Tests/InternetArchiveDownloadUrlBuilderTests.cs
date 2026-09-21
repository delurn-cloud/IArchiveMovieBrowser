using System;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class InternetArchiveDownloadUrlBuilderTests
{
    // --- Normal URL construction and canonical form ---

    [Theory]
    [InlineData("classic_cartoons", "big_buck_bunny.mp4")]
    [InlineData("some-item-123", "video.mkv")]
    [InlineData("item.with.dots", "file.name.webm")]
    public void BuildDownloadUri_YieldsCanonicalHttpsUrl(string identifier, string filename)
    {
        var uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("archive.org", uri.Host);
        Assert.Equal(FormatEscapedPath(identifier, filename), uri.AbsolutePath);
    }

    [Fact]
    public void BuildDownloadUri_UsesHttpsSchemeAndArchiveOrgHost()
    {
        var uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri("item", "movie.mp4");

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("archive.org", uri.Host);
        Assert.StartsWith("https://archive.org/download/", uri.ToString());
    }

    // --- Safe encoding of special characters ---

    [Fact]
    public void BuildDownloadUri_EncodesSpacesParenthesesAndApostrophes()
    {
        const string identifier = "my item (2022)'s";
        const string filename = "documentary (final cut)'s.mp4";

        var uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);

        AssertUrlEncoded(uri, identifier, filename);
    }

    [Fact]
    public void BuildDownloadUri_EncodesUnicodeAsUtf8()
    {
        const string identifier = "caf\u00e9\u6771\u4eac";
        const string filename = "\u6620\u753b du soir.mp4";

        var uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);

        AssertUrlEncoded(uri, identifier, filename);
    }

    [Fact]
    public void BuildDownloadUri_KeepsMultiDotFilenames()
    {
        const string identifier = "item";
        const string filename = "film.2024.bdrip.mkv";

        var uri = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename);

        AssertUrlEncoded(uri, identifier, filename);
    }

    private static void AssertUrlEncoded(Uri uri, string identifier, string filename)
    {
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("archive.org", uri.Host);
        Assert.Equal(FormatEscapedPath(identifier, filename), uri.AbsolutePath);
    }

    private static string FormatEscapedPath(string identifier, string filename) =>
        "/download/" + Uri.EscapeDataString(identifier) + "/" + Uri.EscapeDataString(filename);

    // --- Rejection of blank / traversal / path / absolute URL values ---

    [Theory]
    [InlineData(null, "movie.mp4")]
    [InlineData("", "movie.mp4")]
    [InlineData("   ", "movie.mp4")]
    [InlineData("item/id", "movie.mp4")]
    [InlineData("item\\id", "movie.mp4")]
    [InlineData("a..b", "movie.mp4")]
    [InlineData("https://archive.org/details/x", "movie.mp4")]
    public void BuildDownloadUri_RejectsInvalidIdentifiers(string? identifier, string filename)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename));
    }

    [Theory]
    [InlineData("item", null)]
    [InlineData("item", "")]
    [InlineData("item", "   ")]
    [InlineData("item", "dir/file.mp4")]
    [InlineData("item", "dir\\file.mp4")]
    [InlineData("item", "trail..mp4")]
    [InlineData("item", "http://archive.org/x/f.mp4")]
    [InlineData("item", "https://archive.org/x/f.mp4")]
    public void BuildDownloadUri_RejectsInvalidFilenames(string identifier, string? filename)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename));
    }
}

