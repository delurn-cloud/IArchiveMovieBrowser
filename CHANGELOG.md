# Changelog

User-facing changes to IArchiveMovieBrowser, newest first. Entries describe observable
behavior; they do not list internal refactors.

Work that has not shipped yet is grouped under **Unreleased**. When a release is cut, rename
that heading to the release version and date, and start a fresh empty **Unreleased** section
above it.

## 1.6.0 - 2026-10-14

### Added

- **Bounded Subjects preview in the Details window.** Items with very large subject lists previously
  pushed the rest of the Details view out of sight. Subjects are now shown **one entry per row**, so a
  value containing commas is never confused with the boundary between two entries. The collapsed view
  shows the first **25** entries in a bounded, scrollable list, followed by a plain-text count of what
  is hidden, for example "…and 3,466 more subjects."

  A single extremely long subject value is shown whole — it is one entry, and splitting it on commas
  or semicolons would invent entries the item does not have. Hidden counts always count actual
  entries.

- **Show all / Show fewer** beside the Subjects block. **Show all** displays every subject entry, one
  per row. The list stays bounded and scrolls internally in **both** states, so these buttons always
  remain visible — on an item with thousands of entries the expanded view no longer pushes the
  control that collapses it out of reach. The toggle is keyboard-reachable and labelled in text, and
  the preview returns to collapsed whenever the item's metadata refreshes.

- **Copy subjects** beside the Subjects block. Copies the **complete** list to the clipboard, one
  entry per line with each entry's content preserved — not just the visible preview. Success or
  clipboard failure is reported as plain text; a locked clipboard is handled as an expected failure
  rather than surfacing an unhandled exception.

  This is a **presentation** change only. It does not filter search, alter or delete metadata, judge
  an item's quality, or add any item-specific exclusion.

- **Keywords** filter, under **Narrow results** in the main window. Narrow results by descriptive
  words or phrases in Internet Archive metadata. Separate terms with spaces or commas, and quote a
  multiword phrase to search those words together — `"country house"`, `"Agatha Christie"` — instead
  of matching each word separately. Keywords on their own are enough to run a search, with no title
  required. They are retained across **Previous** / **Next** paging and **Refresh**.

  Keywords are matched **literally**. There is no synonym expansion, stemming, or automatic
  broadening, and nothing you type can become Internet Archive query syntax. A keyword match means
  an uploader recorded that text in the field; it is not a verified classification, so results are
  only as good as the metadata.

- **Search in** option for keywords: **Subjects** (the default) matches the Internet Archive `subject`
  field, which is a short controlled list of descriptive words an uploader chose. **Subjects and
  descriptions** additionally matches the free-text `description` field, which finds more material but
  also matches books, newspapers, and unrelated uploads whose prose happens to mention the words.

- **Match** option for keywords: **All terms** (every term must match), **Any terms** (at least one),
  or **Exact phrase** (the whole entry is treated as a single phrase, equivalent to quoting it).

- **Exclude words** filter: hides items whose metadata contains any of the listed words or phrases.
  It uses the same quoting rules as **Keywords**, and always excludes **every** term you list, so an
  item must match none of them. Excluded words on their own are enough to run a search.

  A quoted phrase that is not closed correctly — an unmatched quote, a quote inside a word such as
  `un"quoted`, text immediately after a closing quote, or an empty `""` — is now **rejected** in
  both boxes. The search does not run, a message names what to correct, and your text is never
  silently reinterpreted as a different search. Corrected text clears the message automatically.

- **Video collections** filter with honest, collection-based labels: **All watchable video** (the
  default; adds no collection restriction), **TV collections** (keeps items in the best-known
  Internet Archive television collections), and **Exclude known TV collections** (removes them).

  The Internet Archive has no separate television media type — television episodes are filed as
  video and are recognisable only by collection — so these options **narrow collections**, and the
  separate **Search scope** choice still decides whether non-video items are included. They are not
  a dependable Movies-only / Television-only split: some television sits in other collections, and
  some non-television items sit in these. The app does not claim otherwise.

- **Order by** filter: **Internet Archive default** (the default; the Internet Archive's own ordering,
  deliberately not labelled "Relevance" because its semantics are not verified) or **Most
  downloaded**. Items with equal download counts are then ordered by identifier so they appear
  consistently. This is not a frozen snapshot, and results can still shift between pages as items
  are added, removed, or change download count. Ordering is retained across paging and **Refresh**.

- **Clear all filters** action, under **Narrow results**. Resets every filter in that section —
  keywords, field selection, match mode, excluded words, video collections, ordering, credited-to,
  year, genre, and trailer exclusion — to its default. It keeps your main search text and your
  **Search scope** choice, does not start a search, and can be used repeatedly.

- An **active-filter** line next to **Clear all filters** reporting which new filters are set, as
  plain text rather than color alone. Every new control has an accessible name, help text, and a
  tooltip; validation appears as text in an assertive live region.

### Changed

- The **Playable video files** list in the Details window is now bounded in height. Items with many
  playable files — for example multi-episode series with one file per episode — previously grew the
  list until it pushed the rest of the Details view out of reach. The list now shows roughly four
  rows and scrolls the rest with its own scrollbar. A short list still sizes to its own contents and
  does not reserve an empty area. Keyboard navigation, selected-file scrolling, file order,
  selection, classification, playback, and downloads are unchanged, and everything below the list
  remains reachable.

- The Year filters now state in their help text that adding a year silently excludes items that carry
  no year value, which is a large share of matches.

## 1.5.1 - 2026-10-09

### Added

- **Exclude trailers** filter, under **Narrow results** in the main window. It is a checkbox that
  is off by default and takes effect when you run a search or refresh, matching how the other
  narrow filters behave. It is retained across **Previous** / **Next** paging and **Refresh**, and
  it works alongside the existing title, scope, creator/credit, year, and Genre filters.

  When enabled, a search omits items that match any of these Internet Archive metadata or title
  rules:

  - the `movie_trailers` collection,
  - the `trailers` subject,
  - the singular `trailer` subject,
  - any title containing the word "trailer".

  **This filter is deliberately aggressive, and enabling it can hide full movies.** Because the
  last rule is a plain title-field match, it also excludes mixed uploads that include trailers,
  such as a feature uploaded "with original trailers", and titles that merely contain the word,
  such as *Trailer Park Boys*, which are not trailers at all. It also does not promise to remove
  every trailer, teaser, TV spot, or game promo: those disappear only when they match the rules
  above. Leave the filter off when you want to see everything the Internet Archive holds for a
  search.

  Identifying trailers this way has known limits. Internet Archive exposes no canonical "is a
  trailer" flag, so the filter relies on collection, subject, and title metadata that can be
  incomplete, inconsistent, or simply wrong. Some trailers are filed under unremarkable titles and
  carry no trailer metadata, so they remain in results. It never inspects item descriptions, video
  duration, or per-item metadata, which keeps it to a single search request with no extra network
  calls.