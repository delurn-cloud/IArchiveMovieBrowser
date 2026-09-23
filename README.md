# IArchiveMovieBrowser

IArchiveMovieBrowser is a Windows desktop application for discovering public movie and video items in the Internet Archive. It focuses on one practical workflow: search and narrow results, inspect an item's details, then either open a selected playable file in a configured external player or download a single selected video file to your computer.

The application is an independent project that uses public Internet Archive search metadata and file-delivery endpoints. It is not affiliated with, endorsed by, or supported by the Internet Archive, and it is not an official Internet Archive application.

[View the main window with Genre filters](docs/images/main-window-genre.png)

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

- **Made by / credited to** — narrow by a name or organization in Internet Archive creator metadata, such as a director, producer, studio, uploader, or curator. This does not search actors or cast.
- **Year** — filter by an exact year or a year range (From / To).
- **Genre** — choose one or more of the curated checklist. Clearing the selection removes the genre constraint with **Clear genres**.

### Genre
- The **Genre** checklist offers a fixed set of curated, user-facing choices such as Comedy, Documentary, Horror, and Science fiction.
- Selecting two or more genres matches items that satisfy **any** selected genre (an alternatives match), not all of them.
- Each genre choice maps to a matching Internet Archive `subject` metadata phrase. The list is a set of suggestions for searching, not a complete or standardized movie-genre database.
- Because Genre relies on `subject` metadata supplied to the Internet Archive, results are only as good as the metadata attached to each item.

### Paging and Refresh
- The Search Results window shows a result-count and current-page summary.
- **Previous** and **Next** move between pages, and **Refresh** re-runs the current search. Genre and other narrow filters are retained across paging and Refresh.

### The Search Results window
Searching from the main window opens a separate Search Results window listing the matching items, each showing its title along with available date/year and creator metadata. Select an item and choose **Open Details** to inspect it.

[View a completed filtered search](docs/images/search-results-part-1-expanded-view.png)

[View the Search Results window](docs/images/search-results-part-2.png)

This screenshot will show a typical results list with the year/creator metadata, the paging controls, and the **Open Details** action.

## Details and playback

Opening Details shows the item's facts (title, identifier, creator, date/year, media type, collections, subjects, and license when supplied), an Internet Archive image preview when one is available, and the item description.

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
- The application does not authenticate, use stored credentials or cookies, or attempt to bypass any access restrictions.
- The application is independent and is not affiliated with, endorsed by, or supported by the Internet Archive.

## How to use it

1. In the main window, enter a title phrase and choose the desired **Search scope** (the default is **Watchable video**).
2. Optionally expand **Narrow results**.
3. Use **Made by / credited to**, **Year**, and **Genre** to narrow the search.
4. With **Genre**, you can select two or more choices; results can match any selected genre, not necessarily all of them.
5. Run the search and open the results.
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

## Project status

The application is under active development.

Current scope is a focused Windows tool that searches public Internet Archive movie/video material, narrows results by creator/credit, year, and curated Genre choices, browses item details, plays a selected video in a configured external player, and downloads a single selected video file with safe overwrite handling. There is no authentication, no bulk or queued downloading, and no resume or history features.

## License

License information has not yet been added.

## Acknowledgments

IArchiveMovieBrowser depends on public Internet Archive search metadata and file-delivery endpoints. This is an independent project and is not affiliated with or endorsed by the Internet Archive.