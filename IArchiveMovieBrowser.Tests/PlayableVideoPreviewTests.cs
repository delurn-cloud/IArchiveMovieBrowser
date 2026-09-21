using System;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class PlayableVideoPreviewTests
{
    [Fact]
    public void Placeholder_IsExactText()
    {
        Assert.Equal(
            "Select a playable video file to preview its direct stream URL.",
            PlayableVideoPreview.SelectionPlaceholder());
        Assert.Equal(
            "Select a playable video file to preview its direct stream URL.",
            PlayableVideoPreview.SelectionPlaceholderText);
    }

    [Fact]
    public void NullOrBlankIdentifier_FallsBackToPlaceholder()
    {
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build(null, "a.mp4"));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("", "a.mp4"));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("   ", "a.mp4"));
    }

    [Fact]
    public void NullOrBlankFilename_FallsBackToPlaceholder()
    {
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("item", null));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("item", ""));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("item", "   "));
    }

    [Fact]
    public void InvalidPathlikeValues_FallBackToPlaceholder_DoNotThrow()
    {
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("a/b", "x.mp4"));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("item", "dir/x.mp4"));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("item", "a..b"));
        Assert.Equal(PlayableVideoPreview.SelectionPlaceholderText, PlayableVideoPreview.Build("https://x", "a.mp4"));
    }

    [Fact]
    public void Build_ReturnsSameUrlAsExistingBuilder()
    {
        const string identifier = "item-123";
        const string filename = "film.mp4";

        string expected = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename).ToString();
        Assert.Equal(expected, PlayableVideoPreview.Build(identifier, filename));
    }

    [Fact]
    public void Build_EncodesSpacesPunctuationUnicode_PerExistingBuilder()
    {
        const string identifier = "my item (2022)'s";
        const string filename = "documentary (final cut)'s \u00e9\u6771\u4eac.mp4";

        // The preview must exactly match the committed builder's URL (its escaping/encoding
        // rules are authoritative and already tested). This is the correctness contract.
        string expected = InternetArchiveDownloadUrlBuilder.BuildDownloadUri(identifier, filename).ToString();
        string actual = PlayableVideoPreview.Build(identifier, filename);

        Assert.Equal(expected, actual);
        Assert.Contains("https://archive.org/download/", actual);
    }

    [Fact]
    public void Build_ChangesWithSelectionFilename()
    {
        const string identifier = "item";

        string urlA = PlayableVideoPreview.Build(identifier, "a.mp4");
        string urlB = PlayableVideoPreview.Build(identifier, "b.mp4");

        Assert.NotEqual(urlA, urlB);
        Assert.Contains("a.mp4", urlA);
        Assert.Contains("b.mp4", urlB);
    }
}