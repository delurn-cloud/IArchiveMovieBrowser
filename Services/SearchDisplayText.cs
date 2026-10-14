using System.Collections.Generic;
using IArchiveMovieBrowser.Domain;

namespace IArchiveMovieBrowser.Services;

/// <summary>
/// Pure, WPF-independent text and state helpers for the search-results screen.
/// None of these methods touch UI, networking, or JSON; they exist so the display logic
/// can be unit-tested without UI automation.
/// </summary>
public static class SearchDisplayText
{
    public const string UntitledText = "(untitled)";
    public const string UnknownCreatorText = "(unknown creator)";
    public const string UnknownMediaTypeText = "(unknown type)";
    public const string UnknownDateText = "(no date)";
    public const string NoDetailsText = "(no additional details)";
    private const string Separator = " · ";

    /// <summary>Line 1 of a result row: the title, or a human-readable fallback.</summary>
    public static string TitleText(InternetArchiveSearchResult result)
    {
        return string.IsNullOrWhiteSpace(result.Title) ? UntitledText : result.Title;
    }

    /// <summary>Date/year segment; prefers Date, then Year, then a fallback.</summary>
    public static string DateYearText(InternetArchiveSearchResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Date))
        {
            return result.Date;
        }
        if (!string.IsNullOrWhiteSpace(result.Year))
        {
            return result.Year;
        }
        return UnknownDateText;
    }

    /// <summary>Creators joined with ", ", or a fallback when absent.</summary>
    public static string CreatorsText(InternetArchiveSearchResult result)
    {
        if (result.Creators is null || result.Creators.Count == 0)
        {
            return UnknownCreatorText;
        }

        string text = "";
        bool first = true;
        foreach (string creator in result.Creators)
        {
            if (string.IsNullOrWhiteSpace(creator))
            {
                continue;
            }
            if (!first)
            {
                text = text + ", ";
            }
            text = text + creator;
            first = false;
        }
        return text.Length == 0 ? UnknownCreatorText : text;
    }

    /// <summary>Media-type segment, or a fallback when absent.</summary>
    public static string MediaTypeText(InternetArchiveSearchResult result)
    {
        return string.IsNullOrWhiteSpace(result.MediaType) ? UnknownMediaTypeText : result.MediaType;
    }

    /// <summary>
    /// Line 2 of a result row: date/year, creator(s), and media type joined without empty
    /// separators (each segment always carries a fallback when its field is missing).
    /// </summary>
    public static string SecondLineText(InternetArchiveSearchResult result)
    {
        var parts = new List<string>
        {
            DateYearText(result),
            CreatorsText(result),
            MediaTypeText(result)
        };

        string text = "";
        bool first = true;
        foreach (string part in parts)
        {
            if (!first)
            {
                text = text + Separator;
            }
            text = text + part;
            first = false;
        }
        return text;
    }

    /// <summary>Complete two-line text for one result ListView row.</summary>
    public static string RowText(InternetArchiveSearchResult result)
    {
        return TitleText(result) + "\n" + SecondLineText(result);
    }

    /// <summary>Result-count and current-page summary, e.g. "128 results · Page 2".</summary>
    public static string ResultsSummary(long numFound, int page)
    {
        return numFound.ToString() + " results" + Separator + "Page " + page.ToString();
    }

    public static string StatusLoading() => "Loading…";
    public static string StatusReady(int page) => "Ready — showing page " + page.ToString() + ".";
    public static string StatusNoResults() => "No results.";
    public static string StatusCancelled() => "Cancelled.";
    public static string StatusError(string message) => "Error: " + message;
    public static string EmptyQueryText() => "Enter a search term.";

    // --- Compact launcher (MainWindow) helpers --------------------------------------

    public const string LauncherTitleText = "Search Internet Archive";
    public const string LauncherInstructionText =
        "Search public Internet Archive movie and video titles.";

    public static string SearchingText() => "Searching Internet Archive…";

    public static string FoundText(long numFound) =>
        "Search complete — " + numFound.ToString() + " matching items found.";

    public static string NoMatchingItemsText() => "No matching Internet Archive items found.";

    public static string OpenResultsText(long numFound) =>
        "Open Results (" + numFound.ToString() + ")";

    public const string NoResultsLabelText = "No Results";

    /// <summary>Visible label for the optional creator narrow filter (user-facing wording).</summary>
    public const string MadeByCreditedToLabel = "Made by / credited to";

    /// <summary>Visible label for the optional Genre narrow filter section header.</summary>
    public const string GenreLabel = "Genre";

    /// <summary>Visible label for the action that clears all selected genres.</summary>
    public const string ClearGenresLabel = "Clear genres";

    /// <summary>Visible label for the optional trailer-exclusion narrow filter.</summary>
    public const string ExcludeTrailersLabel = "Exclude trailers";

    /// <summary>
    /// Accessible help text for the trailer-exclusion narrow filter (user-facing wording). States
    /// plainly that the filter is aggressive, that it can hide full movies, mixed uploads, and titles
    /// that merely contain the word "trailer", and that it is not a promise to remove every teaser,
    /// TV spot, or game promo.
    /// </summary>
    public const string ExcludeTrailersHelp =
        "Removes trailer entries by Internet Archive collection and subject metadata, and any item " +
        "whose title contains the word trailer. This is aggressive: it can also hide full movies, " +
        "mixed uploads that include trailers, and titles that merely contain the word, such as " +
        "Trailer Park Boys. It does not remove every teaser, TV spot, or game promo; those disappear " +
        "only when they match these rules.";

    /// <summary>
    /// Accessible header text for the Genre section. When genres are active it reports how many are
    /// selected (plain text, not color), so a user still sees that Genre is active even while the
    /// section is collapsed. Returns just <see cref="GenreLabel"/> when nothing is selected.
    /// </summary>
    public static string GenreSectionHeader(int selectedCount)
    {
        if (selectedCount <= 0)
        {
            return GenreLabel;
        }
        return GenreLabel + " (" + selectedCount + " selected)";
    }

    /// <summary>Accessible help text for the creator narrow filter (user-facing wording).</summary>
    public const string MadeByCreditedToHelp =
        "Optionally narrow results by a name or organization in Internet Archive creator metadata, " +
        "such as a director, producer, studio, uploader, or curator. This does not search actors or cast.";

    // --- Discovery filters (keywords, collections, ordering) ---------------------------

    /// <summary>Visible label for the keyword box.</summary>
    public const string KeywordsLabel = "Keywords";

    /// <summary>Visible label for the excluded-terms box.</summary>
    public const string ExcludeWordsLabel = "Exclude words";

    /// <summary>Visible label for the keyword-field selection combo.</summary>
    public const string KeywordFieldsCaption = "Search in";

    /// <summary>Visible label for the keyword match-mode combo.</summary>
    public const string MatchModeCaption = "Match";

    /// <summary>Visible label for the video-collection combo.</summary>
    public const string VideoCollectionsCaption = "Video collections";

    /// <summary>Visible label for the ordering combo.</summary>
    public const string OrderingCaption = "Order by";

    /// <summary>Visible label for the action that resets every Narrow results control.</summary>
    public const string ClearAllFiltersLabel = "Clear all filters";

    public const string SubjectsFieldsLabel = "Subjects";
    public const string SubjectsAndDescriptionsFieldsLabel = "Subjects and descriptions";

    public const string AllTermsMatchModeLabel = "All terms";
    public const string AnyTermsMatchModeLabel = "Any terms";
    public const string ExactPhraseMatchModeLabel = "Exact phrase";

    public const string AllWatchableVideoLabel = "All watchable video";
    public const string TelevisionCollectionsLabel = "TV collections";
    public const string ExcludeKnownTelevisionCollectionsLabel = "Exclude known TV collections";

    public const string InternetArchiveDefaultOrderingLabel = "Internet Archive default";
    public const string MostDownloadedOrderingLabel = "Most downloaded";

    /// <summary>Automation name for the text-based filter validation/guidance message.</summary>
    public const string FilterMessageAutomationName = "Filter message";

    /// <summary>
    /// Shared grammar help for both the keyword and excluded-terms boxes. States the delimiters, the
    /// quoted-phrase rule, that an unmatched quote is rejected rather than reinterpreted, and that a
    /// multiword name or phrase should be quoted to search the words together rather than separately.
    /// </summary>
    public const string PhraseGrammarHelp =
        "Separate terms with spaces or commas. Put a phrase in double quotes to search those words " +
        "together, for example \"country house\" or \"Agatha Christie\" — without the quotes, each " +
        "word is matched separately anywhere in the item's metadata. A quote must enclose a whole " +
        "phrase: an unmatched quote, a quote inside a word, text right after a closing quote, and " +
        "an empty \"\" are all rejected, and the search will not run until you fix them. Excluded " +
        "words use the same rules and always exclude every term you list.";

    /// <summary>Help for the keyword field selection combo, including the incidental-match warning.</summary>
    public const string KeywordFieldsHelp =
        "Subjects searches the Internet Archive subject field, which is a short controlled list of " +
        "descriptive words an uploader chose. Subjects and descriptions also searches the free-text " +
        "description, which finds more material but also matches books, newspapers, and unrelated " +
        "uploads whose prose happens to mention the words. Neither option checks that a keyword " +
        "really describes a film: these matches are incidental, drawn from whatever the uploader " +
        "recorded, and the same words can appear for unrelated reasons.";

    /// <summary>Help for the match-mode combo.</summary>
    public const string MatchModeHelp =
        "All terms requires every term you list to match. Any terms requires at least one. Exact " +
        "phrase treats your whole entry as a single phrase and ignores the separators, which is the " +
        "same result as putting the whole entry in one pair of double quotes.";

    /// <summary>
    /// Help for the video-collection combo. Discloses plainly that these are collection-based labels
    /// and not a dependable movies-versus-television classification, and that the primary Search
    /// scope still controls media type.
    /// </summary>
    public const string VideoCollectionsHelp =
        "The Internet Archive has no separate television media type: television episodes are filed as " +
        "video and can only be recognised by the collections they belong to. TV collections keeps " +
        "items filed in the best-known television collections. Exclude known TV collections removes " +
        "items in those collections. Neither is exact: some television sits in other collections, " +
        "and some non-television items sit in these, so this is not a complete Movies-only or " +
        "Television-only separation. These options only narrow collections — your Search scope " +
        "choice still decides whether non-video items are included at all.";

    /// <summary>
    /// Help for the ordering combo. States the limitation honestly: the tiebreaker makes equal
    /// download counts order consistently within one search, but paging is not a frozen snapshot.
    /// </summary>
    public const string OrderingHelp =
        "Internet Archive default uses the Internet Archive's own ordering, which is not ranked by " +
        "relevance. Most downloaded lists the most-downloaded items first. Items with equal download " +
        "counts are then ordered by identifier, so they appear in a consistent order. This is not a " +
        "frozen snapshot: results can still shift between pages if items are added, removed, or " +
        "change download count while you page.";

    /// <summary>
    /// Guidance for the year control. Pre-existing limitation, restated here because it silently
    /// excludes matches when applied: only a minority of sampled items carry a year value.
    /// </summary>
    public const string YearCoverageHelpSuffix =
        " Internet Archive metadata is incomplete: many matching items carry no year value at all, " +
        "so adding a year quietly excludes them.";

    /// <summary>Ordered keyword-field options; Subjects first, so it is the default.</summary>
    public static IReadOnlyList<SearchKeywordFields> KeywordFieldOptions()
    {
        return new List<SearchKeywordFields>
        {
            SearchKeywordFields.Subjects,
            SearchKeywordFields.SubjectsAndDescriptions
        };
    }

    /// <summary>Ordered match-mode options; All terms first, so it is the default.</summary>
    public static IReadOnlyList<SearchKeywordMatchMode> MatchModeOptions()
    {
        return new List<SearchKeywordMatchMode>
        {
            SearchKeywordMatchMode.AllTerms,
            SearchKeywordMatchMode.AnyTerms,
            SearchKeywordMatchMode.ExactPhrase
        };
    }

    /// <summary>Ordered video-collection options; All watchable video first, so it is the default.</summary>
    public static IReadOnlyList<VideoCollectionScope> VideoCollectionOptions()
    {
        return new List<VideoCollectionScope>
        {
            VideoCollectionScope.AllWatchableVideo,
            VideoCollectionScope.TelevisionCollections,
            VideoCollectionScope.ExcludeKnownTelevisionCollections
        };
    }

    /// <summary>Ordered ordering options; Internet Archive default first, so it is the default.</summary>
    public static IReadOnlyList<SearchOrdering> OrderingOptions()
    {
        return new List<SearchOrdering>
        {
            SearchOrdering.InternetArchiveDefault,
            SearchOrdering.MostDownloaded
        };
    }

    public static string KeywordFieldsLabel(SearchKeywordFields fields)
    {
        return fields == SearchKeywordFields.SubjectsAndDescriptions
            ? SubjectsAndDescriptionsFieldsLabel
            : SubjectsFieldsLabel;
    }

    public static string MatchModeLabel(SearchKeywordMatchMode mode)
    {
        return mode switch
        {
            SearchKeywordMatchMode.AnyTerms => AnyTermsMatchModeLabel,
            SearchKeywordMatchMode.ExactPhrase => ExactPhraseMatchModeLabel,
            _ => AllTermsMatchModeLabel
        };
    }

    public static string VideoCollectionsLabel(VideoCollectionScope scope)
    {
        return scope switch
        {
            VideoCollectionScope.TelevisionCollections => TelevisionCollectionsLabel,
            VideoCollectionScope.ExcludeKnownTelevisionCollections => ExcludeKnownTelevisionCollectionsLabel,
            _ => AllWatchableVideoLabel
        };
    }

    public static string OrderingLabel(SearchOrdering ordering)
    {
        return ordering == SearchOrdering.MostDownloaded
            ? MostDownloadedOrderingLabel
            : InternetArchiveDefaultOrderingLabel;
    }

    /// <summary>
    /// A clear, corrective validation message naming the offending box and what to change. The entry
    /// is never silently reinterpreted, so the message always describes an action the user can take.
    /// </summary>
    public static string KeywordEntryErrorText(string boxLabel, SearchKeywordParseError error)
    {
        string box = string.IsNullOrWhiteSpace(boxLabel) ? "Keywords" : boxLabel.Trim();
        return box + ": " + (error switch
        {
            SearchKeywordParseError.UnterminatedQuote =>
                "a quote is missing its closing partner. Add the closing quote, or remove the opening quote.",
            SearchKeywordParseError.EmptyQuotedPhrase =>
                "an empty \"\" quoted phrase has no text to search. Put words between the quotes, or remove them.",
            SearchKeywordParseError.EmbeddedQuote =>
                "a quote appears inside a word. Quotes must enclose a whole phrase, for example \"country house\".",
            SearchKeywordParseError.TextAfterClosingQuote =>
                "text follows a closing quote. Put a space or comma after the closing quote, or quote the whole phrase.",
            SearchKeywordParseError.UnexpectedQuoteInPhrase =>
                "a quote character is not allowed here. Quotes must enclose the whole phrase and cannot be nested or escaped.",
            _ => "the entry could not be read."
        });
    }

    /// <summary>
    /// One-line active-state disclosure for the discovery filters, used by the section header and by
    /// assistive technology. Plain text, never color alone.
    /// </summary>
    public static string DiscoverySectionHeader(int activeCount)
    {
        if (activeCount <= 0)
        {
            return "Discovery";
        }

        return "Discovery (" + activeCount + " set)";
    }

    /// <summary>The Recent summary line used by the Search Results window.</summary>
    public static string QuerySummaryText(string query, long numFound, int page) =>
        "Query: \"" + query + "\"" + Separator + ResultsSummary(numFound, page);

    /// <summary>
    /// Summary line for a completed search built from structured criteria. The legacy part is
    /// produced exactly as before — for a title search it matches the legacy format exactly, and for
    /// actor-only/year-only searches it shows a concise description of those criteria — and the
    /// discovery segments are then <b>appended</b> to it. When no discovery dimension is active the
    /// output is byte-identical to the previous behaviour.
    /// </summary>
    public static string QuerySummaryText(SearchCriteria criteria, long numFound, int page)
    {
        string legacy = "Query: " + LegacyQueryLabel(criteria);
        string discovery = DiscoverySummaryText(criteria);

        string label = discovery.Length == 0 ? legacy : legacy + ", " + discovery;
        return label + Separator + ResultsSummary(numFound, page);
    }

    /// <summary>
    /// The legacy query label: the quoted title, or a concise actor/year description. Extracted so
    /// the discovery segments can be appended without altering any existing wording.
    /// </summary>
    private static string LegacyQueryLabel(SearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.Title))
        {
            return "\"" + criteria.Title + "\"";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(criteria.Creator))
        {
            parts.Add("creator \"" + criteria.Creator!.Trim() + "\"");
        }
        string yearSummary = YearSummary(criteria);
        if (yearSummary.Length > 0)
        {
            parts.Add(yearSummary);
        }

        return parts.Count == 0 ? "criteria" : string.Join(", ", parts);
    }

    /// <summary>
    /// The appended discovery segments, or an empty string when no discovery dimension is active.
    /// Only non-default choices are described, so a default-valued criteria adds nothing.
    /// </summary>
    public static string DiscoverySummaryText(SearchCriteria criteria)
    {
        var segments = new List<string>();

        if (criteria.Keywords is not null && criteria.Keywords.Count > 0)
        {
            segments.Add("keywords " + QuotedTerms(criteria.Keywords)
                + " (" + MatchModeLabel(criteria.MatchMode)
                + ", " + KeywordFieldsLabel(criteria.KeywordFields) + ")");
        }

        if (criteria.ExcludedTerms is not null && criteria.ExcludedTerms.Count > 0)
        {
            segments.Add("excluding " + QuotedTerms(criteria.ExcludedTerms));
        }

        if (criteria.VideoCollections != VideoCollectionScope.AllWatchableVideo)
        {
            segments.Add(VideoCollectionsLabel(criteria.VideoCollections));
        }

        if (criteria.Ordering != SearchOrdering.InternetArchiveDefault)
        {
            segments.Add("ordered by " + OrderingLabel(criteria.Ordering));
        }

        return string.Join("; ", segments);
    }

    /// <summary>Number of discovery dimensions currently set, for the section-header disclosure.</summary>
    public static int ActiveDiscoveryCount(SearchCriteria criteria)
    {
        int count = 0;
        if (criteria.Keywords is not null && criteria.Keywords.Count > 0)
        {
            count++;
        }
        if (criteria.ExcludedTerms is not null && criteria.ExcludedTerms.Count > 0)
        {
            count++;
        }
        if (criteria.VideoCollections != VideoCollectionScope.AllWatchableVideo)
        {
            count++;
        }
        if (criteria.Ordering != SearchOrdering.InternetArchiveDefault)
        {
            count++;
        }
        return count;
    }

    /// <summary>Joins terms for display, quoting any term that contains a space.</summary>
    private static string QuotedTerms(IReadOnlyList<string> terms)
    {
        var parts = new List<string>();
        foreach (string term in terms)
        {
            parts.Add(term.IndexOf(' ') >= 0 ? "\"" + term + "\"" : term);
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Concise human summary of the year range for the Results query line: exact endpoints read
    /// as <c>year 1953</c>, differing endpoints as <c>years 1950–1955</c>, a from-only as
    /// <c>from 1950</c>, and a to-only as <c>through 1955</c>. Empty when no year is supplied.
    /// </summary>
    private static string YearSummary(SearchCriteria criteria)
    {
        if (criteria.YearFrom is not null && criteria.YearTo is not null)
        {
            if (criteria.YearFrom == criteria.YearTo)
            {
                return "year " + criteria.YearFrom.ToString();
            }
            return "years " + criteria.YearFrom.ToString() + "–" + criteria.YearTo.ToString();
        }
        if (criteria.YearFrom is not null)
        {
            return "from " + criteria.YearFrom.ToString();
        }
        if (criteria.YearTo is not null)
        {
            return "through " + criteria.YearTo.ToString();
        }
        return "";
    }

    // --- Search scope selection (launcher) ----------------------------------------

    public const string ScopeLabelText = "Search scope";
    public const string WatchableVideoScopeLabel = "Watchable video";
    public const string RelatedMaterialsScopeLabel = "Related materials";
    public const string EverythingScopeLabel = "Everything";

    public const string RelatedMaterialsTooltip =
        "Non-video Internet Archive items matching your search, such as texts, audio, software, or images.";

    /// <summary>
    /// Supplemental guidance for the launcher's primary Title/Search box: describes the
    /// discover-and-refine workflow without exposing raw IA/Lucene query syntax.
    /// </summary>
    public const string SearchHelpText =
        "Searches titles as the phrase you enter.\n\n" +
        "You can enter part of a title to discover likely matches, then search again using a full title for more precise results.";

    /// <summary>Human-readable label for a scope, matching the launcher ComboBox items.</summary>
    public static string ScopeLabel(SearchScope scope)
    {
        return scope switch
        {
            SearchScope.WatchableVideo => WatchableVideoScopeLabel,
            SearchScope.RelatedMaterials => RelatedMaterialsScopeLabel,
            _ => EverythingScopeLabel
        };
    }

    /// <summary>
    /// The ordered scope options as shown in the launcher, with Watchable video first so it
    /// is the default selection.
    /// </summary>
    public static IReadOnlyList<SearchScope> ScopeOptions()
    {
        return new List<SearchScope>
        {
            SearchScope.WatchableVideo,
            SearchScope.RelatedMaterials,
            SearchScope.Everything
        };
    }

    /// <summary>Previous is enabled on any page after the first.</summary>
    public static bool PreviousEnabled(int page) => page > 1;

    /// <summary>Next is enabled while more pages exist after the current one.</summary>
    public static bool NextEnabled(long numFound, int page, int pageSize)
    {
        return numFound > (long)page * (long)pageSize;
    }
}