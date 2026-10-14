using System;
using System.Collections.Generic;
using System.Linq;
using IArchiveMovieBrowser.Domain;
using IArchiveMovieBrowser.Services;
using Xunit;

namespace IArchiveMovieBrowser.Tests;

public sealed class DetailsDisplayTextTests
{
    // --- Subjects presentation -------------------------------------------------------
    //
    // Presentation only: the preview bounds what is INLINE, while the complete original list stays
    // reachable through the expanded view and the clipboard payload. These tests pin that the
    // complete list is never lost, never split, and never silently reordered.

    private static List<string> Subjects(int count, string prefix = "subject")
    {
        var values = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(prefix + "-" + i.ToString());
        }
        return values;
    }

    [Fact]
    public void Subjects_NullAndEmptyAreHandled()
    {
        Assert.Empty(DetailsDisplayText.SubjectsPreview(null));
        Assert.Empty(DetailsDisplayText.SubjectsPreview(new List<string>()));
        Assert.Equal(0, DetailsDisplayText.SubjectsTotalCount(null));
        Assert.Equal(0, DetailsDisplayText.SubjectsHiddenCount(null));
        Assert.Equal("", DetailsDisplayText.SubjectsFullText(null));
        Assert.Equal("", DetailsDisplayText.SubjectsCopyPayload(null));
        Assert.Equal("", DetailsDisplayText.SubjectsHiddenCountText(0));
    }

    [Fact]
    public void Subjects_AllBlankInputIsHandled()
    {
        var blank = new List<string> { "", "   ", "\t", " \t " };

        Assert.Empty(DetailsDisplayText.SubjectsPreview(blank));
        Assert.Equal(0, DetailsDisplayText.SubjectsTotalCount(blank));
        Assert.Equal(0, DetailsDisplayText.SubjectsHiddenCount(blank));
        Assert.Equal("", DetailsDisplayText.SubjectsCopyPayload(blank));
    }

    [Fact]
    public void Subjects_BlankEntriesAreIgnoredButRealOnesAreCounted()
    {
        var values = new List<string> { "british", "", "  ", "mystery", null! };

        Assert.Equal(2, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(new[] { "british", "mystery" }, DetailsDisplayText.SubjectsPreview(values));
        Assert.Equal("british\nmystery", DetailsDisplayText.SubjectsCopyPayload(values));
    }

    [Fact]
    public void Subjects_ExactlyTheLimitIsNotShortened()
    {
        List<string> values = Subjects(DetailsDisplayText.SubjectsPreviewLimit);

        Assert.Equal(25, DetailsDisplayText.SubjectsPreviewLimit);
        Assert.Equal(25, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(0, DetailsDisplayText.SubjectsHiddenCount(values));
        Assert.Equal("", DetailsDisplayText.SubjectsHiddenCountText(0));

        // Every one of the 25 is in the preview, first and last.
        Assert.Equal(25, DetailsDisplayText.SubjectsPreview(values).Count);
        Assert.Equal("subject-0", DetailsDisplayText.SubjectsPreview(values)[0]);
        Assert.Equal("subject-24", DetailsDisplayText.SubjectsPreview(values)[24]);
    }

    [Fact]
    public void Subjects_OneOverTheLimitHidesExactlyOneEntry()
    {
        List<string> values = Subjects(DetailsDisplayText.SubjectsPreviewLimit + 1);

        Assert.Equal(26, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(1, DetailsDisplayText.SubjectsHiddenCount(values));
        Assert.Equal("…and 1 more subjects.", DetailsDisplayText.SubjectsHiddenCountText(1));

        IReadOnlyList<string> preview = DetailsDisplayText.SubjectsPreview(values);
        Assert.Equal(25, preview.Count);
        Assert.Equal("subject-24", preview[24]);
        Assert.DoesNotContain("subject-25", preview, StringComparer.Ordinal);

        // The complete list still holds all 26, one per line.
        Assert.Equal(26, DetailsDisplayText.SubjectsCopyPayload(values).Split('\n').Length);
    }

    [Fact]
    public void Subjects_HiddenCountCountsActualEntriesNotCharacters()
    {
        List<string> values = Subjects(40, prefix: new string('x', 50));

        Assert.Equal(40, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(15, DetailsDisplayText.SubjectsHiddenCount(values));
        Assert.Equal("…and 15 more subjects.", DetailsDisplayText.SubjectsHiddenCountText(15));
    }

    [Fact]
    public void Subjects_ASingleVeryLongEntryIsOneEntryAndIsNotSplit()
    {
        // One value containing commas and semicolons: it is ONE subject entry. Splitting it would
        // manufacture entries the item does not have.
        string huge = "word" + string.Join(";", Enumerable.Range(0, 400)) + ",trailing,phrase";
        var values = new List<string> { "british", huge };

        Assert.Equal(2, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(0, DetailsDisplayText.SubjectsHiddenCount(values));

        // Shown whole inside the preview; the delimiters survive verbatim.
        Assert.Equal(new[] { "british", huge }, DetailsDisplayText.SubjectsPreview(values));

        // And it is exactly one line in the copy payload, not 400.
        string[] lines = DetailsDisplayText.SubjectsCopyPayload(values).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(huge, lines[1]);
    }

    [Fact]
    public void Subjects_ASingleVeryLongEntryStillCountsAsOneWhenOverTheLimit()
    {
        var values = Subjects(26);
        values.Insert(0, new string('z', 5000));

        Assert.Equal(27, DetailsDisplayText.SubjectsTotalCount(values));
        Assert.Equal(2, DetailsDisplayText.SubjectsHiddenCount(values));
        Assert.Equal("…and 2 more subjects.", DetailsDisplayText.SubjectsHiddenCountText(2));

        // The long value is entry 1 and stays in the preview intact.
        Assert.Equal(new string('z', 5000), DetailsDisplayText.SubjectsPreview(values)[0]);
    }

    [Fact]
    public void Subjects_FullListPreservesOrderAndContentExactly()
    {
        var values = new List<string> { "zeta", "alpha", "Mixed Case", "with,comma", "with;semicolon", "  padded  " };

        string full = DetailsDisplayText.SubjectsFullText(values);
        string[] lines = full.Split('\n');

        Assert.Equal(6, lines.Length);
        Assert.Equal("zeta", lines[0]);
        Assert.Equal("alpha", lines[1]);
        Assert.Equal("Mixed Case", lines[2]);
        Assert.Equal("with,comma", lines[3]);
        Assert.Equal("with;semicolon", lines[4]);
        Assert.Equal("  padded  ", lines[5]); // content preserved verbatim, not trimmed
    }

    [Fact]
    public void Subjects_CopyPayloadIsTheCompleteListNotThePreview()
    {
        List<string> values = Subjects(300);

        string payload = DetailsDisplayText.SubjectsCopyPayload(values);
        string[] lines = payload.Split('\n');

        Assert.Equal(300, lines.Length);            // complete list
        Assert.Equal("subject-299", lines[299]);   // last entry present
        Assert.Equal("subject-0", lines[0]);

        // The preview is strictly smaller, proving copy did not fall back to it.
        Assert.True(DetailsDisplayText.SubjectsPreview(values).Count < lines.Length);
        Assert.Equal(275, DetailsDisplayText.SubjectsHiddenCount(values));
    }

    [Fact]
    public void Subjects_CopyResultTextIsPlainAndCounted()
    {
        Assert.Equal("Copied 300 subjects to the clipboard.",
            DetailsDisplayText.CopySubjectsSuccessText(300));
        Assert.Contains("Could not copy", DetailsDisplayText.CopySubjectsFailureText(), StringComparison.Ordinal);
        Assert.Equal("No subjects to copy.", DetailsDisplayText.CopySubjectsNothingToCopyText());
    }

    [Fact]
    public void Subjects_ExpandedViewUsesTheSameEntriesAsTheCompleteList()
    {
        // The expanded list binds to NonBlankSubjects, so the rows on screen are exactly the lines
        // the copy payload produces - no separate formatting, and nothing lost between the two.
        var values = new List<string> { "News", "breakingnews", "12, tribe", "Mandela's", "Russian news" };

        IReadOnlyList<string> expanded = DetailsDisplayText.NonBlankSubjects(values);
        string[] copyLines = DetailsDisplayText.SubjectsCopyPayload(values).Split('\n');

        Assert.Equal(copyLines.Length, expanded.Count);
        for (int i = 0; i < copyLines.Length; i++)
        {
            Assert.Equal(copyLines[i], expanded[i]);
        }

        // Entries containing commas stay one row rather than being visually ambiguous when joined.
        Assert.Equal("12, tribe", expanded[2]);
    }

    [Fact]
    public void Subjects_ToggleLabelsAreDistinctAndPlainText()
    {
        Assert.Equal("Show all", DetailsDisplayText.ShowAllSubjectsLabel);
        Assert.Equal("Show fewer", DetailsDisplayText.ShowFewerSubjectsLabel);
        Assert.Equal("Copy subjects", DetailsDisplayText.CopySubjectsLabel);
        Assert.NotEqual(DetailsDisplayText.ShowAllSubjectsLabel, DetailsDisplayText.ShowFewerSubjectsLabel);
    }

    [Fact]
    public void Subjects_PreviewLimitAndLineTargetAreStable()
    {
        // The five-line target that the window turns into a concrete height.
        Assert.Equal(5, DetailsDisplayText.SubjectsPreviewLines);
    }

    [Fact]
    public void Subjects_CustomLimitIsRespected()
    {
        List<string> values = Subjects(10);

        Assert.Equal(7, DetailsDisplayText.SubjectsHiddenCount(values, limit: 3));
        Assert.Equal(10, DetailsDisplayText.SubjectsHiddenCount(values, limit: 0));
        Assert.Equal(0, DetailsDisplayText.SubjectsHiddenCount(values, limit: 100));
        Assert.Empty(DetailsDisplayText.SubjectsPreview(values, limit: 0));
    }

    [Fact]
    public void Subjects_ExpandedViewAlwaysReproducesTheCompleteList()
    {
        List<string> values = Subjects(250);

        // The expanded rendering is exactly SubjectsFullText: no preview limit applies, so no
        // entry can be lost when the toggle is on.
        Assert.Equal(string.Join("\n", values), DetailsDisplayText.SubjectsFullText(values));
        Assert.Equal(
            DetailsDisplayText.SubjectsFullText(values).Split('\n').Length,
            DetailsDisplayText.SubjectsCopyPayload(values).Split('\n').Length);
    }
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