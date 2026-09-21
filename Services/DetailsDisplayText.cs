using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Pure, WPF-independent display logic for the item Details window. Reuses the committed
/// <see cref="InternetArchiveDownloadUrlBuilder"/> classification core rather than
/// duplicating extension sets.
/// </summary>
public static class DetailsDisplayText
{
    private const string Separator = " · ";

    public const string MissingText = "(not supplied)";
    public const string UnknownTypeText = "(unknown type)";
    public const string UnknownKindText = "(other)";
    public const string UnknownSizeText = "(unknown size)";
    public const string MediaCandidateYesText = "Yes";
    public const string MediaCandidateNoText = "No";
    public const string ViewLicenseLabel = "View license";

    // --- Metadata fallbacks / joined value helpers --------------------------------

    public static string TitleText(InternetArchiveItemMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.Title) ? MissingText : metadata.Title;

    public static string DateYearText(InternetArchiveItemMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Date))
        {
            return metadata.Date;
        }
        if (!string.IsNullOrWhiteSpace(metadata.Year))
        {
            return metadata.Year;
        }
        return MissingText;
    }

    public static string MediaTypeText(InternetArchiveItemMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.MediaType) ? UnknownTypeText : metadata.MediaType;

    public static string JoinedText(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return MissingText;
        }

        string text = "";
        bool first = true;
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            if (!first)
            {
                text = text + ", ";
            }
            text = text + value;
            first = false;
        }
        return text.Length == 0 ? MissingText : text;
    }

    /// <summary>License display: a readable "View license" label plus the actual URL.</summary>
    public static string LicenseText(InternetArchiveItemMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.LicenseUrl))
        {
            return MissingText;
        }
        return ViewLicenseLabel + " — " + metadata.LicenseUrl;
    }

    // --- File display (classification reuses the committed core) ------------------

    public static string KindText(InternetArchiveRemoteFile file)
    {
        InternetArchiveFileKind kind = InternetArchiveDownloadUrlBuilder.ClassifyFileKind(file.Name);
        return kind switch
        {
            InternetArchiveFileKind.Video => "Video",
            InternetArchiveFileKind.Audio => "Audio",
            _ => UnknownKindText
        };
    }

    public static string MediaCandidateText(InternetArchiveRemoteFile file)
    {
        bool candidate = IsMediaCandidate(file);
        return candidate ? MediaCandidateYesText : MediaCandidateNoText;
    }

    public static bool IsMediaCandidate(InternetArchiveRemoteFile file)
    {
        InternetArchiveFileKind kind = InternetArchiveDownloadUrlBuilder.ClassifyFileKind(file.Name);
        return kind switch
        {
            InternetArchiveFileKind.Video => true,
            InternetArchiveFileKind.Audio => true,
            _ => false
        };
    }

    public static string SizeText(long? size)
    {
        if (size is null)
        {
            return UnknownSizeText;
        }

        long bytes = (long)size;
        if (bytes < 1024)
        {
            return bytes.ToString() + " B";
        }
        if (bytes < 1024L * 1024L)
        {
            return (bytes / 1024.0).ToString("#.#") + " KB";
        }
        if (bytes < 1024L * 1024L * 1024L)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("#.#") + " MB";
        }
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("#.#") + " GB";
    }

    /// <summary>True when at least one entry is a video/audio media candidate.</summary>
    public static bool AnyMediaCandidate(IReadOnlyList<InternetArchiveRemoteFile> files)
    {
        if (files is null)
        {
            return false;
        }
        foreach (InternetArchiveRemoteFile file in files)
        {
            if (IsMediaCandidate(file))
            {
                return true;
            }
        }
        return false;
    }

    // --- Description: safe HTML-to-plain-text conversion --------------------------

    private static readonly Regex HtmlCommentRegex =
        new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptStyleRegex =
        new Regex(@"<(script|style)\b[^>]*>.*?</\s*\1\s*>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Converts an Internet Archive description, which may contain HTML markup, into safe,
    /// readable plain text. No HTML is rendered, no links are activated, and attributes are
    /// never surfaced.
    /// </summary>
    public static string DescriptionText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return MissingText;
        }

        string text = description;

        // B. Remove HTML comments.
        text = HtmlCommentRegex.Replace(text, " ");

        // C. Remove script/style blocks including their contents.
        text = ScriptStyleRegex.Replace(text, " ");

        // D. Convert structural markup into separators/newlines BEFORE generic tag removal.
        // br variants.
        text = Regex.Replace(
            text,
            @"<br\s*/?>",
            "\n",
            RegexOptions.IgnoreCase);
        // paragraph, div, and list-item endings plus headings.
        text = Regex.Replace(
            text,
            @"</?(?:p|div|li)\b[^>]*>",
            "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            @"</h[1-6]\s*>",
            "\n",
            RegexOptions.IgnoreCase);
        // opening headings do not add a newline, but the closing one above does.

        // E. Strip remaining actual HTML tags and attributes.
        text = Regex.Replace(text, @"<[^>]+>", "");

        // F. Decode HTML entities AFTER tag stripping (so decode-produced "<tag>" is text,
        //    never re-stripped).
        text = DecodeHtmlEntities(text);

        // G. Normalize ordinary whitespace, keep structural newlines, reduce repeated blank
        //    lines, and trim.
        text = NormalizeWhitespace(text);

        return string.IsNullOrWhiteSpace(text) ? MissingText : text;
    }

    private static string DecodeHtmlEntities(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                int semi = text.IndexOf(';', i);
                if (semi > 0 && semi <= i + 10)
                {
                    string entity = text.Substring(i + 1, semi - (i + 1));
                    string? decoded = TryDecodeEntity(entity);
                    if (decoded is not null)
                    {
                        builder.Append(decoded);
                        i = semi;
                        continue;
                    }
                }
            }
            builder.Append(text[i]);
        }
        return builder.ToString();
    }

    private static string? TryDecodeEntity(string entity)
    {
        switch (entity)
        {
            case "amp":
                return "&";
            case "lt":
                return "<";
            case "gt":
                return ">";
            case "quot":
                return "\"";
            case "apos":
                return "'";
            case "nbsp":
                return " ";
        }

        if (entity.Length > 2 && entity[0] == '#')
        {
            string number = entity.Substring(1);
            int radix = 10;
            if (number.Length > 1 && (number[0] == 'x' || number[0] == 'X'))
            {
                number = number.Substring(1);
                radix = 16;
            }

            if (int.TryParse(number, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int codepoint) && radix == 10
                && codepoint > 0 && codepoint <= 0x10FFFF)
            {
                return ConvertToChar(codepoint);
            }

            if (radix == 16)
            {
                if (int.TryParse(number, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hex)
                    && hex > 0 && hex <= 0x10FFFF)
                {
                    return ConvertToChar(hex);
                }
            }
        }

        return null; // unknown named entity: leave as literal text
    }

    private static string ConvertToChar(int codepoint)
    {
        return char.ConvertFromUtf32(codepoint);
    }

    private static string NormalizeWhitespace(string text)
    {
        // Collapse runs of spaces/tabs into a single space.
        string collapsed = Regex.Replace(text, "[ \t]+", " ");
        // Trim each line, then trim the whole block.
        string[] lines = collapsed.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Trim();
        }
        string joined = string.Join("\n", lines);

        // Collapse runs of newlines into a single newline (removes repeated blank lines
        // while retaining useful structural line breaks between sections).
        string reduced = Regex.Replace(joined, @"\n{2,}", "\n");
        return reduced.Trim('\n', ' ', '\t', '\r');
    }

    // --- Status texts -------------------------------------------------------------

    public static string StatusLoadingDetails() => "Loading details…";
    public static string StatusReadyDetails() => "Ready";
    public static string StatusNoUsableMedia() => "No usable media files found.";
    public static string StatusError(string message) => "Error: " + message;
    public static string StatusCancelled() => "Cancelled";
}