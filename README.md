# IArchiveMovieBrowser

IArchiveMovieBrowser is a Windows desktop application for discovering public movie and video items in the Internet Archive. It focuses on one practical workflow: search and narrow results, inspect an item's details, then either open a selected playable file in a configured external player or download a single selected video file to your computer.

The application is an independent project that uses public Internet Archive search metadata and file-delivery endpoints. It is not affiliated with, endorsed by, or supported by the Internet Archive, and it is not an official Internet Archive application.

[View the main window before searching](docs/images/main-window-genre.png)

[View the compact-height main window with scrolling](docs/images/main-window-genre-scroll.png)

The main window keeps its controls in one compact, vertically scrollable column: when the window content is taller than the visible area, a vertical scrollbar appears so every control stays reachable. This screenshot will show the expanded Genre checklist and the scroll-discovery hint that points you to the lower controls.

## Why it exists

Rather than navigating public Internet Archive material inside a broad, general-purpose media library application, IArchiveMovieBrowser gives you a small, purpose-built view: start with a title phrase, refine with a few narrow filters, open the matching items, and then play or download what you select. The intent is to keep that search → results → details → playback-or-download path simple and predictable.

### A note from the developer

I originally built IArchiveMovieBrowser for my own quality of life. I wanted a simple, focused way to browse public Internet Archive movies and videos without depending on a larger media application, extra browser add-ons, or a more complicated workflow than the task required.

After using it myself, I thought it might also be useful to other people who prefer small, purpose-built tools: search for a title, narrow the results, inspect an item, then play or download one selected file. The project is shared in that spirit—not as a replacement for every media application, but as a focused option for this particular job.

## Features

### Title search
- Enter a **title phrase** to search public Internet Archive movie and video titles.
- You can enter part of a title to discover likely matches, then search again with a fuller title for more precise results.

### Search scope
- The search scope menu offers three options:
  - **Watchable video** — the default; restricts results to movie/video items.
  - **Related materials** — non-video items such as texts, audio, software, or images.
  - **Everything** — no media-type restriction.
- How results behave depends on which scope you choose. With the default **Watchable video** scope, results are constrained to video that is appropriate for direct viewing.

### Narrow results
Expand **Narrow results** from the main window to refine a search:

- **Keywords** — narrow by descriptive words or phrases in Internet Archive metadata. Separate terms with spaces or commas. Quote a phrase to search those words together, for example `"country house"` or `"Agatha Christie"`. Keywords on their own are enough to run a search.
- **Search in** — choose **Subjects** (the default; the short controlled vocabulary an uploader chose) or **Subjects and descriptions** (also searches free-text description, which finds more but also matches unrelated uploads). See below.
- **Match** — **All terms** (every term must match), **Any terms** (at least one), or **Exact phrase** (the whole entry is one phrase).
- **Exclude words** — hide items whose metadata contains any of the listed words or phrases. Uses the same quoting rules as **Keywords**, and always excludes every term you list. Excluded words on their own are enough to run a search.
- **Video collections** — optional television-collection narrowing. See below.
- **Order by** — **Internet Archive default** or **Most downloaded**. See below.
- **Made by / credited to** — narrow by a name or organization in Internet Archive creator metadata, such as a director, producer, studio, uploader, or curator. This does not search actors or cast.
- **Year** — filter by an exact year or a year range (From / To).
- **Genre** — choose one or more of the curated checklist. Clearing the selection removes the genre constraint with **Clear genres**.
- **Exclude trailers** — off by default; removes trailer entries. See below before enabling it.
- **Clear all filters** — resets every filter in this section to its default. Your main search text and **Search scope** choice are kept. It does not start a search.

An **active-filter** line appears next to **Clear all filters**, reporting which of the new filters are set as plain text.

### Keywords and phrase entry
- Keywords are matched **literally**. There is no synonym expansion, stemming, or automatic broadening of any kind.
- In **Subjects** mode each term is matched against the Internet Archive `subject` field, which is a multi-valued list. Entering `country house` unquoted matches items carrying *each word anywhere* in that list — **broader than the phrase**. Entering `"country house"` matches the phrase.
- In **Exact phrase** mode the whole entry is one phrase, so `Agatha Christie` behaves the same as `"Agatha Christie"`. Internal commas are literal.
- A quote must enclose a **whole phrase**. An unmatched quote, a quote inside a word (`un"quoted`), text immediately after a closing quote (`"a"b`), and an empty `""` are **rejected**. The search does not run until you fix them, and the entry is never silently reinterpreted as a different search. Quotes do not nest, and there is no quote-escape syntax.
- **Exclude words** uses the same rules and the same rejections.
- In **Subjects and descriptions** mode each term is matched against `subject` **or** `description`, then **All**/**Any** combines the per-term groups. Because `description` is unstructured free text, this matches more material — including books, newspapers, and unrelated uploads whose prose happens to mention the words.

### Video collections
- The Internet Archive has **no separate television media type**: television episodes and series are filed as video and can only be recognised by the **collections** they belong to. These options therefore narrow collections:
  - **All watchable video** — the default; adds no collection restriction at all, and includes television.
  - **TV collections** — keeps items filed in the best-known television collections (`television`, `classic_tv`, `television_inbox`); an item in any one of them qualifies.
  - **Exclude known TV collections** — removes items in those collections.
- **This is not a dependable Movies-only / Television-only split.** Some television sits in other collections (including user-favourite ones), and some non-television items sit in these, so the separation leaks in both directions. It is a best-known-collection filter, not a classification.
- These options only narrow **collections**. Your **Search scope** choice still decides whether non-video items are included at all.

### Order by
- **Internet Archive default** (the default) uses the Internet Archive's own ordering. It is deliberately **not** called "Relevance", because its ranking semantics are not verified.
- **Most downloaded** lists the most-downloaded items first. Items with equal download counts are then ordered by identifier, so they appear in a consistent order.
- **This is not a frozen snapshot.** Results can still shift between pages if items are added, removed, or change download count while you page.

### Genre
- The **Genre** checklist offers a fixed set of curated, user-facing choices such as Comedy, Documentary, Horror, and Science fiction.
- Selecting two or more genres matches items that satisfy **any** selected genre (an alternatives match), not all of them.
- Each genre choice maps to a matching Internet Archive `subject` metadata phrase. The list is a set of suggestions for searching, not a complete or standardized movie-genre database.
- Because Genre relies on `subject` metadata supplied to the Internet Archive, results are only as good as the metadata attached to each item.

### Exclude trailers
- **Exclude trailers** is an opt-in checkbox, off by default. When enabled it omits items filed under the Internet Archive `movie_trailers` collection, items carrying the `trailers` or `trailer` subject, and any item whose title contains the word "trailer".
- **This filter is aggressive.** Because the title rule is a plain title-field match, it can also hide full movies and mixed uploads that include trailers, such as a feature uploaded "with original trailers." It also hides titles that merely contain the word, such as *Trailer Park Boys*, even though nothing about them is a trailer.
- It does **not** promise to remove every trailer, teaser, TV spot, or game promo. Those disappear only when they match the collection, subject, or title rules described above. Trailers filed under unremarkable titles and items with no trailer metadata at all can still appear.
- Leave it off when you want to see everything the Internet Archive holds for a search, including trailers and mixed uploads.

### Paging and Refresh
- The Search Results window shows a result-count and current-page summary.
- **Previous** and **Next** move between pages, and **Refresh** re-runs the current search. Keywords, match mode, excluded words, video collections, **Order by**, Genre, **Exclude trailers**, and the other narrow filters are all retained across paging and Refresh.
- Paging is **not** a frozen snapshot: the Internet Archive offers no stable result set, so results can shift between pages as items are added or removed.

### The Search Results window
Searching from the main window opens a separate Search Results window listing the matching items, each showing its title along with available date/year and creator metadata. Select an item and choose **Open Details** to inspect it.

[View a completed filtered search](docs/images/search-results-part-1-expanded-view.png)

[View the Search Results window](docs/images/search-results-part-2.png)

This screenshot will show a typical results list with the year/creator metadata, the paging controls, and the **Open Details** action.

## Details and playback

Opening Details shows the item's facts (title, identifier, creator, date/year, media type, collections, subjects, and license when supplied), an Internet Archive image preview when one is available, and the item description.

The **Subjects** block is bounded so a very large subject list cannot overwhelm the view: it shows the first **25** entries, one per row, in a short scrollable list, followed by a count of what is hidden (for example, "…and 3,466 more subjects"). Entries are shown individually rather than joined with commas, so a value containing a comma is never confused with the gap between two entries. Use **Show all** / **Show fewer** to expand or collapse the complete list — the list stays bounded and scrolls internally either way, so those buttons always remain in view — and **Copy subjects** to copy the **whole** list to the clipboard, one entry per line. The preview returns to collapsed when the item's metadata refreshes. This only changes presentation — no subject is hidden permanently, and nothing is filtered or deleted.

The Details window also identifies **Playable video files** among the item's files and lists the full file inventory. Selecting a playable file shows:

- **Direct stream URL** — the canonical Internet Archive source URL for the selected file.
- **Open selected video in external player** — passes the selected file's freshly resolved delivery URL to your configured player.
- **Resolved player URL** — the temporary, resolved delivery URL that is handed to the player.
- **Check selected video link** — a diagnostic that checks the selected file's direct link.

[View the Details overview](docs/images/details-overview.png)

[View a selected playable video in Details](docs/images/details-selected-video.png)

This screenshot will show the Details window with a playable video file selected, including the canonical **Direct stream URL** and the **Resolved player URL**.

## External player notes

- Configure an external media-player executable where the app asks for the player path (for example, **Choose player…** in the main window). Once set, a **Change…** and a **Clear** action become available.
- The app resolves Internet Archive delivery redirects immediately before launching the player, so the player receives a current, working delivery URL. The canonical **Direct stream URL** is retained as the durable source reference.
- Whether a particular video actually plays depends on that player and the codecs installed for it. VLC is a widely used, practical example, but no specific player is required by the application, and the app does not install or configure codecs.
- A file that plays in one player (such as VLC) may not play out of the box in another (such as Windows Media Player) if the needed codec support is not present.

## Download behavior and safety

- **Download selected video…** saves the currently selected playable video file to a destination you choose through the standard Save As dialog.
- The file streams to disk and is not loaded entirely into memory.
- Progress is shown while the transfer runs, and **Cancel download** stops it.
- While a transfer is incomplete, the data is written to a temporary sibling `.partial` file. On success the download is finalized only after the transfer completes; on failure or cancellation the temporary file is cleaned up.
- If a file with the chosen name already exists, the app asks for explicit confirmation before writing. The safe non-overwrite choice is the default, and the existing destination is preserved until a completed replacement is ready.
- The app downloads one selected video file at a time. It does not provide multi-file downloads, ZIP downloads, download queues, history, resume/range downloads, login, or any form of restriction bypassing.

[View download progress](docs/images/details-download-progress.png)

This screenshot will show the download in progress, including the progress indication and the **Cancel download** control.

[View the existing-file replacement confirmation](docs/images/replace-confirmation.png)

This screenshot will show the safe replacement prompt that appears when the chosen destination already exists, with the non-overwrite option as the default.

## Internet Archive metadata and availability

Search results, Genre matching, creator/credit and year filters, image previews, and file information all come from Internet Archive metadata. That metadata can be incomplete, inconsistent, or absent, which affects how well items can be searched, filtered, or displayed.

Keep in mind:

- Some items or files may be unavailable, access-restricted, missing, or unsuitable for a particular player.
- Genre reflects curated choices mapped to Internet Archive `subject` metadata, not a universal movie taxonomy.
- **Keywords** are literal and literal-only. A keyword match means an uploader recorded that text in the field; it is **not** a verified classification. Subject vocabulary is self-reported and inconsistent (`British`, `british`, `england`, `uk`, `British TV` can all coexist and refer to overlapping but non-identical sets), so keyword matches are **incidental** and can be correct for unrelated reasons.
- **Country of production and cast are not dependable structured filters.** They are not offered as controls. `country` values are sparse (in the sample used for design, only a small fraction of video items carried any `country` value at all), and actor names appear in the same undifferentiated `subject` list as directors, composers, and topical subjects. Use them as keywords instead.
- **Video collections** are collection-based labels, not a dependable Movies-only / Television-only split; see [Video collections](#video-collections).
- **Year** filters exclude items that carry no year value at all, which is a large share of matches. Use it to narrow within a result set you already know is relevant, not as a first filter.
- **Exclude trailers** depends on Internet Archive collection, subject, and title metadata, none of which is a canonical "is a trailer" flag. It therefore both misses some trailers and hides some full movies; see [Exclude trailers](#exclude-trailers).
- The application does not authenticate, use stored credentials or cookies, or attempt to bypass any access restrictions.
- The application is independent and is not affiliated with, endorsed by, or supported by the Internet Archive.

## How to use it

1. In the main window, enter a title phrase and choose the desired **Search scope** (the default is **Watchable video**). You can also search using **Keywords** alone, without a title.
2. Optionally expand **Narrow results**.
3. Use **Keywords**, **Search in**, **Match**, **Exclude words**, **Video collections**, **Order by**, **Made by / credited to**, **Year**, **Genre**, and optionally **Exclude trailers** to narrow the search.
4. Quote multiword keywords (`"country house"`) to search them together. If the app shows a filter message, the entry is not run — fix the quoted text it names and search again.
5. With **Genre**, you can select two or more choices; results can match any selected genre, not necessarily all of them. **Exclude trailers** is aggressive, so enable it only when you want trailers, mixed uploads, and titles containing the word "trailer" hidden.
6. Run the search and open the results.
6. Select an item in the Search Results window and choose **Open Details**.
7. In Details, select a **playable video file**.
8. Do one of the following:
   - **Open selected video in external player** to play it in your configured player; or
   - **Download selected video…** to save the selected file locally, using **Cancel download** at any point to stop a transfer.
9. In the main window, if the window content is taller than the visible area, a vertical scrollbar appears. Lower controls such as the search status, **Open Results**, and the external-player settings may be available by scrolling down.

## Build and run from source

IArchiveMovieBrowser targets Windows and is built from source with the .NET toolchain. There is currently no packaged installer or prebuilt release download; build and run from source as described below.

> A configured external player is optional. Search, browsing, and download all work without one; you only need a player when you want to open a video in an external application.

### Requirements
- A Windows machine.
- A .NET SDK that supports the `net10.0-windows` target framework used by this project.
- (Optional) an external media-player executable if you want to play selected videos.

### Build
From the repository root, run:

```powershell
dotnet build IArchiveMovieBrowser.slnx -c Debug
```

### Test
```powershell
dotnet test IArchiveMovieBrowser.slnx -c Debug
```

### Run
```powershell
dotnet run --project IArchiveMovieBrowser.csproj
```

Run this command from the repository root. If you run it from another directory, provide the path to `IArchiveMovieBrowser.csproj`.

For a dated history of user-facing changes, see [CHANGELOG.md](CHANGELOG.md).

## Project status

The application is under active development.

Current scope is a focused Windows tool that searches public Internet Archive movie/video material, narrows results by creator/credit, year, and curated Genre choices, browses item details, plays a selected video in a configured external player, and downloads a single selected video file with safe overwrite handling. There is no authentication, no bulk or queued downloading, and no resume or history features.

## Downloads

IArchiveMovieBrowser supports downloading one user-selected file at a time.

This is intentional. The application is designed for finding, reviewing, playing, or saving individual Internet Archive movie files—not for bulk downloading or mirroring collections. Keeping downloads explicit and single-file helps users choose the format they actually want and is more considerate of Internet Archive as a shared nonprofit resource.

Bulk queues, concurrent downloads, and collection mirroring are outside this project's scope.

## License

The project is licensed under the MIT License.

## Acknowledgments

IArchiveMovieBrowser depends on public Internet Archive search metadata and file-delivery endpoints. This is an independent project and is not affiliated with or endorsed by the Internet Archive.