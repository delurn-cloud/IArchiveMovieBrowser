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
    public void Description_NullAndBlankFallback()
    {
        Assert.Equal("(not supplied)", DetailsDisplayText.DescriptionText(null));
        Assert.Equal("(not supplied)", DetailsDisplayText.DescriptionText(""));
        Assert.Equal("(not supplied)", DetailsDisplayText.DescriptionText("   "));
    }

    [Fact]
    public void Description_PlainTextNormalized()
    {
        Assert.Equal("Simply a plain description.",
            DetailsDisplayText.DescriptionText("Simply a plain description."));
        Assert.Equal("Text with many spaces",
            DetailsDisplayText.DescriptionText("Text     with    many   spaces"));
    }

    [Fact]
    public void Description_StructuralNewlines()
    {
        Assert.Equal("Line A\nLine B", DetailsDisplayText.DescriptionText("Line A<br />Line B"));
        Assert.Equal("Line A\nLine B", DetailsDisplayText.DescriptionText("Line A<p>Line B</p>"));
        Assert.Equal("A\nB\nC", DetailsDisplayText.DescriptionText("A<div>B</div><li>C</li>"));
        Assert.Equal("Heading\nBody", DetailsDisplayText.DescriptionText("<h2>Heading</h2>Body"));
    }

    [Fact]
    public void Description_BoldAndAnchorRemoved_HrefDoesNotLeak()
    {
        const string html = "Visit <a href=\"https://example.com/x\">the site</a> now and <b>read this</b>.";
        const string expected = "Visit the site now and read this.";
        Assert.Equal(expected, DetailsDisplayText.DescriptionText(html));
        Assert.DoesNotContain("https://", DetailsDisplayText.DescriptionText(html));
        Assert.DoesNotContain("example.com", DetailsDisplayText.DescriptionText(html));
    }

    [Fact]
    public void Description_CommonNamedEntities()
    {
        Assert.Equal("A & B <tag> > up \"quote\" 'apos'",
            DetailsDisplayText.DescriptionText("A &amp; B &lt;tag&gt; &gt; up &quot;quote&quot; &apos;apos'"));
    }

    [Fact]
    public void Description_NumericEntities()
    {
        Assert.Equal("Hi\x2019",
            DetailsDisplayText.DescriptionText("Hi&#8217;"));
        Assert.Equal("A\x20AC",
            DetailsDisplayText.DescriptionText("A&#x20AC;"));
    }

    [Fact]
    public void Description_EscapedAngleBracketOrdering()
    {
        // Real tag stripped first; entity-decode happens after, so literal "<tag>" survives.
        Assert.Equal("A & B <tag>", DetailsDisplayText.DescriptionText("A &amp; B &lt;tag&gt;"));
    }

    [Fact]
    public void Description_NestedMixedMarkup()
    {
        const string html =
            "<div class=\"w\"><p><b>Developed by</b> Adventure International</p></div>" +
            "<br/>Second paragraph";
        const string expected =
            "Developed by Adventure International\nSecond paragraph";
        Assert.Equal(expected, DetailsDisplayText.DescriptionText(html));
    }

    [Fact]
    public void Description_WhitespaceAndBlankLinesNormalized()
    {
        const string html = "One<br><br><br>Two";
        Assert.Equal("One\nTwo", DetailsDisplayText.DescriptionText(html));
    }

    [Fact]
    public void Description_CommentsStripped()
    {
        const string html = "Before<!-- hidden -->after";
        Assert.Equal("Before after", DetailsDisplayText.DescriptionText(html));
    }

    [Fact]
    public void Description_ScriptStyleBlocksStripped()
    {
        const string html = "Hello<script>var x=1;</script>world<style>p{color:red}</style>";
        Assert.Equal("Hello world", DetailsDisplayText.DescriptionText(html));
    }

    [Fact]
    public void Description_BuckarooFragmentLeavesNoMarkup()
    {
        const string html =
            "Adventures of Buckaroo Banzai, The (1985)(Adventure International)<br /><br />" +
            "<div class=\"mobygames_description\"><p><b>Developed by</b> Adventure International</p></div>";
        string result = DetailsDisplayText.DescriptionText(html);

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("href=", result);
        Assert.DoesNotContain("class=", result);
        Assert.Contains("Adventures of Buckaroo Banzai", result);
        Assert.Contains("Developed by", result);
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