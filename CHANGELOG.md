# Changelog

User-facing changes to IArchiveMovieBrowser, newest first. Entries describe observable
behavior; they do not list internal refactors.

Work that has not shipped yet is grouped under **Unreleased**. When a release is cut, rename
that heading to the release version and date, and start a fresh empty **Unreleased** section
above it.

## 1.5.1 — 2026-10-09

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