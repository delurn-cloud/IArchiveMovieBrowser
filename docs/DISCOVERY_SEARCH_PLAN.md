# Discovery search

Status: **Tier A and the Details presentation fixes implemented and closed.** The five open decisions
in the original proposal were resolved by the owner and are now settled below (§7). Tier B saved
searches remain deferred. Tier C enrichment and noise suppression remain out of scope.

Immediate acceptance example: finding *British movies and television mysteries*. This is an
acceptance example, **not** the feature. The design is general.

This document records the approved decisions, the correction to the secondary-sort observation, the
implementation that followed, the Details presentation fixes (§10, §13), the validation record (§12),
and the future capability catalogue (§11).

---

## 1. Repository baseline

Observed on `main` before implementation:

| Item | Value |
|---|---|
| HEAD at start | `5f87293` — "Add trailer exclusion filter and prepare version 1.5.1" |
| Working tree | Clean except untracked `Properties/` (pre-existing; left untouched) |
| Target framework | `net10.0-windows`, WPF, `Nullable` enabled |
| Compile model | SDK-style implicit glob — new `.cs` files need no `.csproj` change |
| Tests before | **378 passed, 0 failed, 0 skipped** (~85 ms) |
| Build | `dotnet build IArchiveMovieBrowser.slnx -c Debug` succeeds |
| Latest changelog release | 1.5.1 - 2026-10-09 |

The 378-test baseline was confirmed by running the suite before any edit, and re-confirmed
unchanged after implementation. §9 records the final result.

### Current search surface (inventory)

- **Primary row** (`MainWindow.xaml`): `QueryTextBox` (title), `SearchScopeCombo`,
  `SearchButton`.
- **Narrow results** expander, 4-row grid: Made by / credited to (row 0),
  Year From/To (row 1), Genre expander (row 2), Exclude trailers (row 3).
- **Query builder**: `Services/SearchScopeQueryBuilder.cs` — pure, deterministic,
  fixed clause order: `title:` → `creator:` → `year:` → `subject:` genre OR-group
  → trailer exclusion → scope condition.
- **Criteria**: `Domain/SearchCriteria.cs` — immutable, `WithGenres()` /
  `WithExcludeTrailers()` combinators, `HasAnyCriterion` gate.
- **Scopes** (`Domain/SearchScope.cs`): `WatchableVideo` = `AND mediatype:movies`;
  `RelatedMaterials` = `AND NOT mediatype:movies`; `Everything` = no media clause.
- **Pagination / refresh** (`SearchResultsWindow.xaml.cs`): `RunPageAsync` rebuilds
  `new InternetArchiveSearchRequest(_criteria, page, PageSize)` from the immutable
  `_criteria` captured by `ApplyCompletedSearch`. Only `page` varies.
- **Cancellation**: monotonic `_generation` counter plus `CancellationTokenSource`,
  re-checked after every `await` in both windows. Superseded responses are
  discarded, never merged into visible state.
- **Tests**: 20 xUnit files, all offline via a fake `HttpMessageHandler`. No test
  asserts on live Internet Archive results.

## 2. Investigation findings

### 2.1 What the existing searches query

| Search | Emitted clause | Behaviour |
|---|---|---|
| Title / free text | `title:"<EscapeLiteral(input)>"` | One literal substring of the title field. `\` and `"` are the only characters neutralized. No stemming, no token OR, no metadata fallback. |
| Genre | `(subject:"comedy" OR subject:"western" …)` | Curated catalog (`Domain/GenreDefinition.cs`) → equal-lowercase IA subject phrase, OR-grouped. |
| Credited to | `creator:(Gene AND Barry)` | Whitespace tokens AND-ed inside a `creator:` group; Boolean words and Lucene-sensitive tokens are safely quoted. |

Free text is **title only**. There is no free-text metadata search, no exclusion
capability, and no control separating movies from television.

### 2.2 Live metadata/search observations

All figures below are **point-in-time observations from a bounded sample taken on
2026-10-13**, used to guide design. They are **not regression thresholds** and must
not be asserted against in tests — Internet Archive content changes continuously.
Tests assert on the **emitted query string**, never on a result count.

| Query | Observed count |
|---|---|
| `title:"British mysteries" AND mediatype:movies` | 0 |
| `title:"British mystery movies"` | 0 |
| `mediatype:movies` | 17,368,558 |
| `mediatype:television` | **0** |
| `NOT mediatype:movies` | 110,288,021 |
| `mediatype:movies AND collection:television` | 754,328 |
| `collection:television` / `classic_tv` / `television_inbox` | 754,328 / 11,025 / 91,233 |
| `subject:"british"` / `subject:"mystery"` | 43,566 / 10,618 |
| `subject:"Mystery"` (capitalised) | 10,618 — subject matching is case-insensitive |
| `subject:"british" AND subject:"mystery"` | 373 |
| … plus the current trailer exclusion | 369 |
| … plus `NOT subject:"horror"` | 299 |
| `subject:"british" OR subject:"mystery"` (any-term) | 53,811 |
| `subject:"british mystery"` (exact phrase) | 3 |
| British+mystery with `year:*` populated | 204 of 373 (55%) |
| British, no genre restriction | 43,566 |
| Excluding known TV collections | 310 |
| Restricting to `collection:television` | 63 |
| `country:*` / `country:"United Kingdom"` | 5,020 / 156 |
| `language:*` / `language:eng` | 10,288,023 / 4,108,611 |
| `creator:*` | 9,620,250 |
| `cast` / `actor` / `starring` / `role` / `performers` | 161 / 2,733 / 16 / 0 / 2 |

#### 2.2.1 Secondary-sort support — verified

One bounded check was performed and is recorded here because an earlier draft of this document
described the result **incorrectly**. The corrected single observation:

| Probe | Result |
|---|---|
| `sort[]=downloads desc` vs itself (control) | **0 differences** — output stable run-to-run |
| `sort[]=identifier asc` alone | Works — valid sort key, reorders correctly |
| downloads-only vs downloads+identifier, 200 rows | **12 positional differences, all at the 9 download ties** |
| Within-tie order, downloads-only | violates identifier-ascending at **6 of 9** ties |
| Within-tie order, downloads+identifier | violates identifier-ascending at **0 of 9** ties |
| Escaped vs literal `[]` parameter encoding | Identical — encoding is not a factor |

**Conclusion: secondary sort is supported.** The endpoint accepts the repeated `sort[]` parameter
without error and applies it; within-tie ordering becomes identifier-ascending. An earlier reading
of the first probe — that ordering was "identical, therefore ignored" — was wrong: the top 50 rows
had no download ties, so the first probe was uninformative, and a per-tie comparison initially
attributed the ascending order to the wrong sequence. Both counts above are from the single
corrected check; the mixed intermediate figures are not part of the record.

### 2.3 The Internet Archive has no `mediatype:television`

This is the finding that most changes the design. Television episodes and series
are filed as `mediatype:movies`, distinguished only by collection. Consequences:

- The existing **Watchable video** scope already includes television. The owner's
  "British movies and television mysteries" are reachable in principle today.
- Separating movies from television **must** use `collection:` clauses.
  `mediatype:television` returns zero and would silently match nothing.

### 2.4 Separation by collection is leaky

Items observed under the movies-only variant (which excludes `television`,
`classic_tv`, and `television_inbox`) still included *Miss Marple – 3 episodes with
Joan Hickson*, because that item sits in `fav-tv_series_1960` — a **user favourite**
collection, not a canonical television collection. This is why the plan does not
promise dependable movies/television classification (see §5).

### 2.5 Ordering is the practical discovery blocker

The client currently sends no `sort[]` parameter. The same 373-result set, sorted
by downloads, surfaced recognisable British mysteries (*Miss Marple* 86,931;
*Survivors* 39,269; *Hammer House Of Horror*; *Life on Mars*; *The Prisoner* 1967;
*The Tripods*) where the default order surfaced Romanov-history and news material.
This is why ordering is in Tier A rather than treated as a nicety.

### 2.6 Three distinct false-match mechanisms

1. **Vocabulary-dump pollution.** Item `sharware` has a **single** subject entry
   containing **3,491** semicolon-delimited tokens — a full A–Z vocabulary plus
   search-engine internals. It simultaneously matched `british`, `mystery`,
   `western`, `detective`, `murder`, `comedy`, `documentary`, and `crime`.
2. **Semantic drift.** Item `jane-eyre-2006-ep-3` genuinely carries `British Army`
   and `Western` as subjects; it is an audiobook set on the American frontier.
   The metadata is literally correct; the intent is irrelevant to film genre.
3. **Collection noise.** Television-news items were observed carrying 3,302
   auto-generated subjects, so they match correctly and then rank near the top.

Noise suppression is **out of scope** (Tier C). The mitigation is disclosure, not
filtering — see §4 and §5.

## 3. Proposed scope

### 3.1 Tier A — core controls (in scope)

Each dimension becomes one additional `AND`-joined clause, in a fixed, testable
order:

```
title:"…" → keywords → match mode → excluded terms → creator:(…) → year:[…]
  → (genre OR-group) → [video collection clause] → <trailer exclusion> → scope
```

Ordering is appended to the request as a separate parameter (§3.4).

| Control | Emitted | Rationale |
|---|---|---|
| **Keywords / themes** | `subject:"<term>"` per term | Title-only search cannot express "British mystery" at all |
| **Match mode** | All → `AND` group; Any → `OR` group; Exact phrase → single quoted term | The three behaviours title-only cannot express |
| **Excluded terms** | `NOT subject:"<term>"` per term | Confirmed working (373 → 299 excluding `horror`) |
| **Video collections** | `collection:` clauses (§5) | Only viable TV separation mechanism |
| **Ordering** | `sort[]` parameter (§3.4) | Default order buries the intended results |

Existing **title**, **genre** (OR), **year**, **credited-to**, **scope**, and
**Exclude trailers** are unchanged. All Tier A criteria are server-side and
pagination-safe.

### 3.2 Keyword field selection (resolved)

**Both options are implemented, chosen by the user:**

| Option | Emitted per term |
|---|---|
| **Subjects** (default) | `subject:"<term>"` |
| **Subjects + descriptions** | `(subject:"<term>" OR description:"<term>")` |

Each positive term is OR-ed across the selected fields, and the All/Any match mode
then combines those per-term groups. Exclusions use the selected fields too. This
supersedes the earlier proposal to commit to subjects only: the owner accepted both
targets because the field choice is now an explicit, inspectable control rather than
a hidden default. Incidental matches are disclosed in the field-selection help text.

### 3.3 Term tokenizer, phrase entry, and rejection (resolved)

**Rules — implemented in `SearchKeywordPhraseParser`:**

- Whitespace and commas delimit unquoted terms.
- A quoted phrase is exactly one term.
- **Rejected, not reinterpreted** (each blocks submission with a corrective message):
  unterminated quote, empty quoted phrase `""`, quote embedded inside an unquoted
  token, non-delimiter text immediately after a closing quote, and nested/leftover
  quotes in Exact-phrase mode.
- **Exact phrase** mode: trim outer whitespace, treat the whole entry as one phrase,
  optionally consume one matching outer quote pair, treat internal commas literally,
  reject any remaining quote character. Whitespace-only or delimiter-only input is empty.
- Excluded terms always use the terms tokenizer, independently of the positive match
  mode.
- No nested quotes and no quote-escape syntax in this slice.

This **supersedes** the earlier "degrade safely" rule, which would have silently
reinterpreted `"country house` as a different search. The owner decided invalid input
must instead block submission.

**Worked examples** (emitted clause; `subject:` shown for clarity)

| Input | Mode | Emitted | Note |
|---|---|---|---|
| `country house` | All | `(subject:"country" AND subject:"house")` | **Broader than the phrase.** Documented in help text. |
| `"country house"` | All | `subject:"country house"` | Intended adjacency; single term, no redundant parens. |
| `country house` | Any | `(subject:"country" OR subject:"house")` | Very broad; quotes strongly recommended. |
| `"country house"` | Any | `subject:"country house"` | Quotes pin the phrase regardless of mode. |
| `Agatha Christie` | All | `(subject:"Agatha" AND subject:"Christie")` | **Broad.** May match the author alongside unrelated subjects. |
| `"Agatha Christie"` | All | `subject:"Agatha Christie"` | What a person searching a full name intends. |
| `Agatha Christie` | Exact phrase | `subject:"Agatha Christie"` | Identical to quoting. |
| `British mystery` | All, Subjects+desc | `((subject:"British" OR description:"British") AND (subject:"mystery" OR description:"mystery"))` | Per-term field OR, then AND across terms. |
| `british` | All, Subjects+desc | `(subject:"british" OR description:"british")` | Single term; inner parens are load-bearing, no outer group. |
| `"country house` | any | *(rejected)* | **Blocks submission**; message names the fix. |
| `british "" western` | any | *(rejected)* | Empty quoted phrase. |
| `un"quoted` | any | *(rejected)* | Quote inside a word. |
| `"a"b` | any | *(rejected)* | Text right after a closing quote. |

**Exclusions** are per-term negations joined with `AND` (an item must match *none* of
them). A `NOT (a OR b)` group is never emitted — it would be a no-op that widens results.
Multiword excluded phrases are supported: `"country house"` → `NOT subject:"country house"`.

**No hidden synonym expansion.** Ordinary keyword entry is always literal. Broadening via
synonym OR-groups would exist only as an explicit, inspectable **saved recipe** (§3.5). The
acceptance examples in §6 use literal terms only.

### 3.4 Ordering (resolved)

| Option | Parameter | Notes |
|---|---|---|
| **Internet Archive default** | *(none sent)* | **Default.** Named "Internet Archive default", **not** "Relevance" — relevance semantics were not verified |
| **Most downloaded** | `sort[]=downloads desc` **and** `sort[]=identifier asc` | Downloads descending, with an ascending-identifier tiebreaker so equal download counts resolve deterministically |

The identifier tiebreaker is verified supported (§2.2.1). It makes ties within one
ordering stable; it is **not** a snapshot guarantee — the endpoint offers no frozen result
set, so paging can still shift if items are added, removed, or change download count
between page requests. The app does not claim otherwise, and the ordering help text says so.

Ordering is recorded in `SearchCriteria` so paging and refresh preserve it, exactly as genre
and trailer exclusion already are.

### 3.5 Tier B — saved search recipes (deferred)

Owner decision: deferred until Tier A has been used and its criteria shape is
stable. When pursued, recipes serialise a complete `SearchCriteria` and **must
display their expanded terms before running**, per §3.3. `ApplicationSettingsBase`
already exists for the player path, but a *list* of structured recipes needs a
different storage shape (likely JSON under `%APPDATA%`).

### 3.6 Tier C — out of scope

Country-of-production enrichment, cast/actor enrichment, noise suppression, and
any external metadata source. Detail in §5.

## 4. Metadata limitations

| Dimension | Available source | A match means | Observed coverage | Verdict |
|---|---|---|---|---|
| **Country of production** | `country` | "An uploader recorded a country value" | In this sample, 5,020 of 17.4M movies carried any `country` value; 156 carried `United Kingdom`; `Great Britain` returned 0 | **Not dependable for a structured filter.** Remains a labelled keyword |
| **Language** | `language` | "Audio is in this language" | 10,288,023 of 17.4M carried a value; `eng` 4,108,611. 4,085,412 English-language movies carried no `british` subject | Usable as a narrow filter; **not a nationality proxy** |
| **Actor / cast** | No structured field observed | Actor names appear as free-text `subject` values | `cast` 161, `actor` 2,733, `starring` 16, `role` 0, `performers` 2 in this sample | **Not dependable for a structured filter.** Remains a labelled keyword |

**Scope of these claims.** These are counts from a **bounded sample**, not a census.
They show that country and cast metadata are **too sparse to build dependable
structured filters on from what is observable here**; they are **not** proof that
such metadata is universally absent from the Internet Archive, and the figures will
drift as items are added, edited, or removed.

**Cast and crew share one undifferentiated bag.** *Blind Date* (1959) carries
`Hardy Krüger`, `Stanley Baker`, `Micheline Presle`, `Joseph Losey` — and also
`Dutch Artist` and `Art Gallery`. A cast-oriented keyword search therefore also
matches directors, composers, and topical subjects.

**Year is not a reliable narrowing filter.** Only 204 of the 373 British-mystery
items sampled populated `year:`. Adding a year silently excludes a large share of
matches. This is pre-existing and must be stated in the year control's help text.

**Subject vocabulary is self-reported and inconsistent.** `British`, `british`,
`england`, `uk`, `united kingdom`, `British comedy films`, and `British TV` all
coexist, referring to overlapping but non-identical sets.

**Keywords are literal and literal-only.** Because subjects are descriptive,
keyword matches are descriptive. The UI must not imply verified classification.

### Client-side filtering

The search path contains exactly one client-side filter:
`InternetArchiveApiClient.TryParseSearchResult` drops any result document whose
`identifier` is missing or blank. This is a defensive guard, **not** narrowing. If it
ever triggered, a 25-row page would render short with no explanation. It should
remain visible in review rather than be mistaken for result filtering.

> **Correction.** An earlier draft of this document recorded that this guard "has no
> dedicated test" and that a test "should be added in Tier A". That was incorrect:
> `InternetArchiveApiClientTests.Search_FiltersDocsWithoutIdentifier` already covers a
> response containing a kept identifier, a missing identifier, and a blank one. The
> coverage existed at HEAD; no new test was added for it in Tier A.

Everything else is server-side in the `q=` parameter.

## 5. Video collections — honest labelling

The Internet Archive has **no `mediatype:television`**. Television is identified
only by collection, and that separation is demonstrably leaky: items filed under
user-favourite collections such as `fav-tv_series_1960` are television but are not
in the canonical television collections.

The plan therefore makes **no dependable movies/television classification promise**
and uses honest collection-based labels.

**Finalised set (both variants, resolved):** `television`, `classic_tv`, `television_inbox`.

| Label | Emitted clause | Honest description |
|---|---|---|
| **All watchable video** | *(no clause)* — **Default** | Everything the primary scope allows, including television |
| **TV collections** | `AND (collection:television OR collection:classic_tv OR collection:television_inbox)` | Items in at least one best-known TV collection (**OR**, so any one qualifies) |
| **Exclude known TV collections** | `AND NOT collection:television AND NOT collection:classic_tv AND NOT collection:television_inbox` | Removes items in those collections; some television remains |

The include variant is an **OR group**: "TV collections" means an item in *any* of them. (An
AND-list would require an item to be in all three at once, which is not the intent.) The set is
app-controlled catalog text in `SearchDiscoveryCatalog`, unit-testable and never assembled from UI
wording.

These options **only narrow collections** — the primary `SearchScope` still controls media type.
The help text and summary say so, so a Related-materials or Everything search is not mistaken for a
video-only one.

`classic_tv_<decade>` collections were observed but are **not** included: §2.4 shows the separation
already leaks via user-favourite collections, so adding decade names narrows in an unverified
direction without fixing the leak.

## 6. Tests

All tests offline via a fake `HttpMessageHandler`, asserting on **emitted query
strings and request parameters**. **No test asserts a result count.** See §9 for results.

### 6.1 Query construction — as implemented

- Match mode: All → parenthesized `AND` group; Any → parenthesized `OR` group;
  Exact phrase → single quoted term; single term → no redundant parentheses.
- Field selection: Subjects → `subject:"…"`; Subjects+descriptions → parenthesized per-term OR,
  combined by the match mode.
- Tokenizer: quoted phrase; **rejection** of unterminated quote, empty `""`, embedded quote, and
  text after a closing quote; comma splitting; whitespace/delimiter-only input emits no clause.
- Multiword cases from §3.3, including `country house` and `Agatha Christie` in
  All, Any, and Exact phrase modes, unquoted and quoted.
- **Literal-only guarantee:** keyword output contains no synonym expansion; Subjects mode never
  mentions `description`.
- **Escaping parity with title:** hostile keyword/exclusion text stays inside one literal phrase;
  backslash escaping; no untrusted text becomes a field, operator, group, or range.
- Exclusion: per-term `NOT subject:"…"`; multi-field `NOT (subject:"…" OR description:"…")`;
  never a top-level `OR` group; empty exclusion emits no clause;
  interaction with the fixed trailer exclusion is positional and preserved.
- Composition: keywords × title × creator × year × genre × video collections ×
  trailer exclusion × all three scopes; deterministic and independent of the order
  in which controls were changed.
- Year: exact, range, from-only, to-only, unchanged from current behaviour.

### 6.2 State preservation

Every new dimension must be exercised across the full lifecycle, not just at
construction:

- **Paging:** identical `q=` on page 1 and page 3 for keywords, match mode,
  exclusions, video collections, and ordering.
- **Refresh:** re-runs the complete specification, including ordering.
- **Cancellation:** a superseded request must not overwrite newer state, and a
  cancelled request must leave prior results intact.
- **Summaries:** `QuerySummaryText` renders keywords, match mode, exclusions,
  video collections, and ordering; correct output when there is no title; legacy
  title-search summary format is preserved.
- **Scope combinations:** each video-collection label against all three scopes.
- **Reset:** *Clear all filters* restores every default, **preserves the title**,
  and is idempotent.
- **Validity:** keywords-only and exclusions-only searches are valid; a fully blank
  search is still rejected.

### 6.3 Defaults (explicit) — see §7.1 for the authoritative table

| Control | Default |
|---|---|
| Title | *(whatever the user typed — preserved by reset)* |
| Search scope | Watchable video — **preserved by reset** |
| Keywords | empty |
| Keyword fields | **Subjects** |
| Match mode | **All terms** |
| Excluded terms | empty |
| Video collections | **All watchable video** |
| Ordering | **Internet Archive default** |
| Exclude trailers | **off** (unchanged) |
| Genre | none selected |
| Year | blank |
| Credited to | empty |

### 6.4 Acceptance cases — as implemented

Asserted on emitted query shape only:

| Case | Input | Expected shape |
|---|---|---|
| British mysteries, all video | `British mystery`, All, Subjects, All watchable video, trailers off | `(subject:"British" AND subject:"mystery") AND mediatype:movies` |
| Same, Subjects + descriptions | same keywords, field option changed | `((subject:"British" OR description:"British") AND (subject:"mystery" OR description:"mystery")) AND mediatype:movies` |
| British mystery movies | `British mystery movies`, All | **Identical** to the first case — a trailing head noun is neither required nor added |
| Quoted phrase | `"British mystery"` | `subject:"British mystery" AND mediatype:movies` (single term, no redundant parens) |
| TV collections | same keywords, **TV collections** | Adds `AND (collection:television OR collection:classic_tv OR collection:television_inbox)` |
| Exclude known TV collections | same keywords, **Exclude known TV collections** | Adds the three `NOT collection:` clauses joined by `AND` |
| Every collection option × every primary scope | all 3 × 3 | Collection clause composes with the media-type clause independently |
| British with exclusion | `British mystery`, **exclude** `horror` | Adds `AND NOT subject:"horror"` |
| Most downloaded | same keywords, **Most downloaded** | `sort[]=downloads desc` **and** `sort[]=identifier asc`, in that order; default sends neither |
| Full stack | all controls engaged | Single deterministic query in the fixed clause order; trailer exclusion still positioned before the scope clause |

No synonym expansion appears in any acceptance example.

## 7. Open decisions — resolved

The five decisions are settled and implemented. Resolution notes:

1. **Subjects-only vs. subjects + description → BOTH implemented.** The owner
   offered both as explicit field-selection options rather than picking one default.
   Subjects is the default; Subjects+descriptions ORs each term across both fields.
   See §3.2.
2. **Stable secondary sort → SUPPORTED and implemented.** `sort[]=identifier asc` is
   appended after `sort[]=downloads desc`. Verified (§2.2.1). Paging is deterministic
   within one ordering but is not a frozen snapshot, and the app does not claim it is.
3. **Final TV collection list → fixed set of three, both variants.** `television`,
   `classic_tv`, `television_inbox` are used consistently for the include **and**
   exclude variants. The include variant ORs the set; the exclude variant negates each
   member joined with `AND`. Classification limits remain disclosed (§5).
4. **Reset scope → confirmed.** *Clear all filters* resets everything inside **Narrow
   results** and preserves the primary-row title **and search scope**. The scope is a
   "how wide is the catalogue" choice, not a narrowing filter.
5. **Final wording → collection-based labels kept**, with an added disclosure that
   these narrow collections while the primary scope still controls media type. No
   dependable Movies-only/Television-only classification is promised.

Carried forward from the investigation:

6. **Ranking enablement** — owner confirmed ordering ships in Tier A. (Done.)
7. **Tier B timing** — saved searches remain deferred until Tier A's criteria shape is
   stable. (Still deferred.)

### 7.1 Defaults (implemented)

| Control | Default |
|---|---|
| Title | whatever the user typed — preserved by reset |
| Search scope | Watchable video — preserved by reset |
| Keywords | empty |
| Keyword fields | **Subjects** |
| Match mode | **All terms** |
| Excluded terms | empty |
| Video collections | **All watchable video** (no collection clause) |
| Ordering | **Internet Archive default** (no sort parameters) |
| Exclude trailers | off (unchanged) |
| Genre | none selected (unchanged) |
| Year | blank (unchanged) |
| Credited to | empty (unchanged) |

Keywords and excluded terms are **primary** dimensions — either alone makes a blank search
valid. Genre and trailer exclusion remain **secondary** and still cannot start a search on
their own (existing behaviour, preserved and tested).

## 8. Files affected — as implemented

**Production (9):** 6 modified, 3 new enum/type files, 2 new logic files.

**Modified (6)**

| # | File | Change |
|---|---|---|
| 1 | `Domain/SearchCriteria.cs` | Add `Keywords`, `KeywordFields`, `MatchMode`, `ExcludedTerms`, `VideoCollections`, `Ordering`; `With…` combinators; compute `HasAnyCriterion` over all primary dimensions |
| 2 | `Services/SearchScopeQueryBuilder.cs` | Emit keyword group, per-field OR, exclusions, and video-collection clauses in the fixed order |
| 3 | `Services/FilterCriteriaLogic.cs` | Accept discovery inputs; reject malformed keyword entries as a blocking state |
| 4 | `Services/InternetArchiveApiClient.cs` | Append ordered `sort[]` parameters for non-default ordering |
| 5 | `Services/SearchDisplayText.cs` | New labels, help text, validation messages, appended summary segments, section-header disclosure |
| 6 | `MainWindow.xaml` / `MainWindow.xaml.cs` | New controls, accessibility, active-state text, validation message, Clear-all-filters behaviour |

**New (5)**

| # | File | Change |
|---|---|---|
| 7 | `Domain/SearchKeywordFields.cs` | Subjects / SubjectsAndDescriptions |
| 8 | `Domain/SearchKeywordMatchMode.cs` | AllTerms / AnyTerms / ExactPhrase |
| 9 | `Domain/SearchOrdering.cs` | InternetArchiveDefault / MostDownloaded |
| 10 | `Domain/VideoCollectionScope.cs` | Distinct type from the primary `SearchScope`, by design |
| 11 | `Domain/SearchDiscoveryCatalog.cs` | TV collection set, field→clause mapping, sort-parameter source of truth |
| 12 | `Domain/SearchKeywordPhraseParser.cs` | Pure phrase grammar and rejection rules |

**Verified unchanged:** `SearchResultsWindow.xaml.cs` — it captures the immutable `_criteria`
and rebuilds `new InternetArchiveSearchRequest(_criteria, page, PageSize)`; because the client
derives both `q` and `sort[]` from the criteria, all discovery dimensions and ordering flow
through paging, refresh, and cancellation with no change. The summary line flows through
`SearchDisplayText`, which was extended instead. No `.csproj` change (SDK implicit glob), no
new dependencies, no playback/download change.

**Tests (1 new file)**

- `IArchiveMovieBrowser.Tests/SearchDiscoveryTests.cs` — 109 tests covering phrase grammar,
  rejection rules, keyword/mode/field query shapes, exclusions, collection × scope, full-stack
  ordering, ordering parameters, paging/refresh identity, validation blocking, validity rules,
  reset/idempotence, summaries, and honest-labelling help text.

**Documentation (3)**

- `docs/DISCOVERY_SEARCH_PLAN.md` (this file)
- `README.md` — features, narrow results, how-to-use, metadata-limitations section
- `CHANGELOG.md` — new **Unreleased** section

**Not required:** no `.csproj` change (SDK-style implicit compile glob); no new
dependencies; no change to playback or download behaviour.

### UI placement and accessibility — as implemented

- Primary row unchanged. New controls sit inside **Narrow results**: keywords (with field
  selection and match mode), excluded terms, video collections, and ordering sit above Genre;
  **Exclude trailers** remains last; **Clear all filters** and the active-state line sit below it.
- The grid auto-sizes and the existing scroll hint handles overflow, so the compact launcher stays
  usable; all controls remain visible (no nested expander was needed).
- Every new control has `AutomationProperties.Name` + `HelpText` and a tooltip, matching the
  existing pattern.
- Active discovery state is reported as **plain text** next to **Clear all filters** (e.g.
  "Discovery (2 set): Keywords, Video collections"), never color alone, reusing the
  `GenreSectionHeader` convention.
- Validation is a text `TextBlock` in an assertive live region (`AutomationProperties.LiveSetting
  =Assertive`), again text-based rather than color-only.
- Multiword-entry guidance, including the quoted-form examples, appears in the keywords and
  excluded-terms help text.

## 9. Verification record

| Command | Result |
|---|---|
| `dotnet build IArchiveMovieBrowser.slnx -c Debug` | **Build succeeded — 0 warnings, 0 errors** (XAML compiled) |
| `dotnet test IArchiveMovieBrowser.slnx -c Debug` (pre-change baseline) | **378 passed, 0 failed, 0 skipped** |
| `dotnet test IArchiveMovieBrowser.slnx -c Debug` (post-change) | **487 passed, 0 failed, 0 skipped** (378 existing + 109 new), ~110 ms |

All tests are offline via a fake `HttpMessageHandler` and assert on emitted query strings, request
parameters, or display text. **No test asserts a live result count.** No live Internet Archive
requests were made during implementation beyond the single authorized secondary-sort check (§2.2.1).

### 9.1 Runtime UI/use validation — owner-reported

The owner launched the application and performed the requested normal-use checks; **those checks
passed**. This is recorded as **owner-reported**, not as automated verification.

What this does and does not cover:

- **Covered by owner manual use:** the normal-use path — launching the launcher, entering keywords,
  choosing fields/match mode/collections/ordering, running searches, paging, **Refresh**, **Clear all
  filters**, and viewing results in the Search Results and Details windows.
- **Still NOT verified:** the WPF generation guard (`if (currentGeneration != _generation) return;`)
  in `MainWindow.ExecuteSearchAsync` and `SearchResultsWindow.RunPageAsync`. A single manual pass
  **cannot establish deterministic superseded-response or race behaviour**; the owner check does not
  license any claim of race coverage. `SupersededRequest_AgeLateResponseCannotReplaceTheNewerSpecificationsState`
  proves immutable state isolation only — that a late older response cannot mutate the newer
  criteria, page, count, or ordering — not that the guard executes.
- No automated regression harness covers window lifetime, cancellation racing, or layout.

Note: all live figures in §2.2 were captured on 2026-10-13 and will drift. They justify design
choices; they are not acceptance thresholds.

---

## 10. Subjects presentation fix — implemented

**Status: implemented and validated offline.** Tier A remains closed and unchanged.

**Observation that prompted it.** During owner manual use, a Details item was found whose Subjects
block is extremely large and overwhelms the view (§2.6 documents a case with 3,491 tokens in a single
subject value; other items carry thousands of entries).

This was a **presentation** problem only. No metadata, API, or search behaviour changed; there is no
noise suppression, no filtering, no metadata deletion, and no item blacklist. Tier C remains out of
scope.

**Behaviour as implemented (corrected after owner runtime review):**

- **One entry per row, not a comma-joined sentence.** The first implementation joined the preview
  with `", "`. Because subject values themselves frequently contain commas, that made it impossible
  to see where one entry ended and the next began. Entries are now individual rows, so each value -
  including ones containing commas or semicolons - is unambiguous and is never split.
- **Bounded in BOTH states - the critical correction.** The first implementation lifted the height
  bound when expanded, reasoning that the window's own scrollbar would cope. On an item with 3,302
  entries this pushed **Show fewer** thousands of lines below the viewport, making the control that
  exits the expanded state effectively unreachable. Owner screenshots confirmed this. The list is now
  bounded in both states (collapsed 130 DIP, expanded 260 DIP) with its own scrollbar, so
  **Show all / Show fewer / Copy subjects are permanently visible** without scrolling the window.
  This is the one place a nested scroll area is *necessary* rather than unnecessary.
- **Bounded preview** - the first `SubjectsPreviewLimit` (**25**) actual entries when collapsed.
  Hidden counts are disclosed as plain text and always count actual entries, never characters.
- **One very long entry is one entry.** A value containing commas or semicolons is displayed and
  copied verbatim and is never split, so no entry is manufactured.
- **The height clipping notice was removed.** The earlier design *clipped* text and disclosed it.
  The corrected design scrolls within a bounded list instead, so nothing is clipped. Removing it also
  removed the layout-measurement code and the ambiguity around inferring overflow from `ActualHeight`
  reaching `MaxHeight`, which cannot prove overflow on its own.
- **Show all / Show fewer** - a keyboard-reachable, text-labelled `ToggleButton`. The preview resets
  to collapsed whenever metadata refresh completes.
- **Copy subjects** - copies the **complete** original list, one entry per line, preserving each
  entry's content (including interior spacing). Success and clipboard failure are reported as plain
  text; a locked clipboard is caught as an expected `ExternalException` / `ThreadStateException`
  rather than an unhandled exception.

**Automated coverage** (`DetailsDisplayTextTests`): null/empty and all-blank input; blank entries
ignored while real ones counted; exactly 25 and 26 entries; a single very long entry with embedded
`;` and `,`; hidden counts by actual entry; full-list order and content preservation; copy payload
completeness vs. the preview; custom limits; copy result wording; toggle labels; and that the
expanded rows are exactly the copy-payload lines, so the two views cannot drift apart.

**Not covered by automation:** the toggle/collapse **state machine** and the bounded list's layout live
in `DetailsWindow.xaml.cs` / `DetailsWindow.xaml` and are WPF-layout dependent. The pure helpers are
tested; the on-screen list behaviour was exercised by the owner in normal use. See §12.

**Owner runtime review — first implementation rejected, cause identified and fixed:** owner
screenshots of `ChristopherColumbusAndTheAfricanHolocaust` (3,302 subject entries) showed the
expanded view filling the entire window with **no visible way back**: `Show fewer` sat thousands of
lines below the viewport. Root cause was the unbounded expanded state described above, compounded by
the comma-joined format making entry boundaries unreadable. Both are corrected.

**Owner acceptance: accepted and closed.** The owner tested the corrected result and confirmed it
satisfactory. The revised design is owner-tested in normal use — see §12 for exactly what that
established and, explicitly, what it did not.

---

## 11. Future capability catalogue (planning inventory — not implemented)

Recorded so future capabilities stay documented as *future*, not impossible. This catalogue was
reviewed and accepted as a planning inventory; **none of it is implemented in Tier A.** Class 1
(build now) was exactly the Tier A scope above. Class 2 = future enhancement with a named
requirement. Class 3 = unsupported/unreliable on current evidence.

**Mechanism:** **S** = server-side query (`q=`/`sort[]`); **P** = per-item metadata
(`/metadata/{id}`); **L** = local app state.

### A. Text, phrase, Boolean grouping, field selection
| Capability | Mech | Class | Note |
|---|---|---|---|
| Title literal phrase | S | 1 | Existing. |
| Keywords over metadata | S | 1 | Implemented (subjects and/or descriptions). |
| Field selection | S | 1 | Implemented: Subjects / Subjects+descriptions. |
| All/Any/Exact-phrase grouping | S | 1 | Implemented. |
| Quoted-phrase entry | S | 1 | Implemented, with rejection of malformed quotes. |
| Raw Lucene passthrough | S | 3 | Rejected: breaks the literal-only guarantee. |
| Wildcards/regex/proximity/boosting | S | 3 | Rejected: same reason. |

### B. People, country vs. setting, language, dates
| Capability | Mech | Class | Note |
|---|---|---|---|
| Credited to (`creator:`) | S | 1 | Existing. |
| Cast/actor search | S | 3 | No structured field; names share the undifferentiated `subject` bag. |
| Director/crew distinction | S | 3 | Same root cause. |
| Country of production | S | 2 | Requirement: observed coverage too sparse for a dependable filter (5,020 of 17.4M). Remains a labelled keyword in Tier A. |
| Setting/location | S | 3 | No structured field; would be subject-bag guessing. |
| Language filter | S | 2 | Requirement: verify exact code list and multi-value behaviour; usable but **not** a nationality proxy. |
| Release year | S | 1 | Existing; sparse (204 of 373 sampled). |
| Release `date:` vs sparse `year:` | S | 2 | Requirement: verify `date:` coverage/format; cheap quality win if better populated. |
| Added/uploaded date (`addeddate`) | S | 2 | Requirement: verify range syntax. Archive-controlled, likely the one dependable date axis ("recently added"). |
| Public date (`publicdate`) | S | 2 | Same family; lower value. |

### C. Collections, material types, series/episodes
| Capability | Mech | Class | Note |
|---|---|---|---|
| Search scope | S | 1 | Existing. |
| Video-collection variants | S | 1 | Implemented (television / classic_tv / television_inbox). |
| Movies-only vs Television-only | S | 3 | No `mediatype:television`; collection separation leaks. Not promised. |
| Arbitrary user-chosen collection | S | 2 | Requirement: need a collection enumeration source. |
| Series/episode grouping | S | 3 | No verified structured TV-series field. |
| Episode co-location by uploader convention | S | 2 | Mechanism: `identifier:prefix_*`; requirement: verify, and label as convention-dependent. |
| Search within a collection | S | 2 | Depends on collection picker landing first. |

### D. Runtime, files, format, resolution, subtitles, access
| Capability | Mech | Class | Note |
|---|---|---|---|
| "Watchable video" media restriction | S | 1 | Existing. |
| Per-item playable-file detection | P | 1 | Already implemented (`PlayableVideoClassifier`). |
| Format display | P | 1 | Already available in file inventory. |
| Runtime filter / resolution filter | — | 3 | No server-side field; per-item would need N+1 metadata calls. Structural limit, not an evidence gap. |
| Runtime / resolution display | P | 2 | Requirement: parse file `length`/`width`/`height` (not currently parsed). Display only. |
| Format filter | S | 3 | Format labels are item-dependent/inconsistent; would exclude valid items. |
| Subtitles filter | S | 3 | No dependable server-side field. |
| Subtitles display | P | 2 | Requirement: detect subtitle/closed-caption files; state "none found" honestly. |
| Access-restriction indicator | S | 2 | Requirement: verify `access-restricted-item`; recommend **label, not filter**. |
| Loanable/print-disabled state | — | 3 | Per-item and mutable; not reliably queryable. |

### E. Ordering, related searches, history, saved/shareable
| Capability | Mech | Class | Note |
|---|---|---|---|
| Default + Most downloaded ordering | S | 1 | Implemented (with verified identifier tiebreaker). |
| Further IA sorts | S | 2 | Requirement: verify each; sorts on uploader `date` inherit sparse-metadata problems. |
| Facet counts → "related searches" | S | 2 | Requirement: verify `facet[]` support; if absent, degrades to per-item inspection. |
| Search history | L | 2 | Requirement: list storage (scalar settings insufficient), retention/privacy decision. |
| Saved searches | L | 2 | Tier B, deferred. Must render expanded terms before running. |
| Shareable search link | L | 2 | Requirement: versioned criteria serialization; depends on Tier B. |
| Recently viewed / continue watching | L | 3 | Player feature, not search. |

### F. Summaries, reset, missing metadata, zero results
| Capability | Mech | Class | Note |
|---|---|---|---|
| Query summary (appended segments) | L | 1 | Implemented; legacy title-only format preserved. |
| Active-filter disclosure | L | 1 | Implemented (plain text, not color). |
| Clear all filters | L | 1 | Implemented (preserves title + scope). |
| Missing-metadata disclosure | L | 1 | Help text. |
| Result-count caution | L | 1 | Trivial disclosure. |
| Zero-result help | L | 2 | Requirement: inspect criteria + `numFound == 0`; cannot distinguish "no such item" from "metadata says so". |

### Not in Tier A (recorded, deferred, out of scope)
Saved searches/history, grouped alternative clauses, added-date filters, facets, extra sorts,
external enrichment (country/cast), file-stat discovery (runtime/resolution/subtitle filtering),
and noise suppression. These are **future**, not universally impossible, except where marked
Class 3 as a structural limit.

---

## 12. Validation status

**Final state: Tier A and the Details presentation fixes are accepted and closed.**

| Area | Status |
|---|---|
| Tier A query/validation/summary behaviour | Automated - offline, 504 tests total |
| Tier A runtime normal use | **Owner-reported** - owner launched the app and the requested normal-use checks passed (§9.1) |
| Tier A WPF generation guard / race behaviour | **Not verified, and not claimed.** The out-of-order test proves immutable state isolation only; the guard itself is never exercised. No deterministic race or superseded-response coverage is asserted |
| Tier A layout, scrolling, accessibility announcement | **Not verified** by automation |
| Subjects presentation - final design | **Owner-tested and accepted.** Collapsed list bounded with its own scrollbar; one entry per row; `Show all` keeps the list bounded so `Show fewer` stays visible without scrolling the window; hidden-count disclosure correct; resets to collapsed on refresh; **Copy subjects** copies the complete list |
| Playable files list bounded height (§13) | **Owner-tested and accepted.** Reduced height and scrollbar observed working; earlier "no scrollbar" report was traced to a stale pre-2026-10-09 binary, not a layout defect |
| **Copy subjects - clipboard failure path** | **Not verified, and not claimed.** The typed `ExternalException` / `ThreadStateException` handlers compile and are plain-text only, but no forced clipboard failure was exercised at runtime |
| **Copy subjects - clipboard success path** | **Owner-tested** as part of the Subjects acceptance |

### 12.1 Scope of the runtime verification

What owner runtime testing established: the launcher and Details presentation behave acceptably in
normal use, and the two presentation fixes work as intended.

What it did **not** establish, and which therefore remains unverified rather than implicitly
approved:

- **Deterministic superseded-response or race behaviour.** A single normal-use pass cannot establish
  this. The WPF generation guard remains unexercised by any test or manual procedure.
- **The clipboard failure path.** No scenario forced a locked clipboard, so the plain-text failure
  message was never observed in the running app.
- Accessibility announcement, and Tier A layout/scrolling details beyond normal use.

### 12.2 Note on stale binaries

> An owner screenshot after the first height change still showed an unbounded list with **no**
> scrollbar, which `MaxHeight` + `VerticalScrollBarVisibility="Auto"` cannot produce. Older
> `bin/Release/net10.0-windows/win-x64` and `publish/win-x64` binaries plus the installer exe still
> date from 2026-10-09. Confirm the running build's timestamp before judging layout behaviour.

---

## 13. Playable video files list - bounded height (implemented and owner-verified)

**Observation.** The **Playable video files** list grew excessively on multi-episode items: a series
item can carry one playable file per episode, and every row was rendered inline.

**Correction.** A narrow layout fix in `DetailsWindow.xaml`:

- `MaxHeight="150"` on the existing `ListView`, **not** `Height`, so a short list sizes to its own
  content and reserves no empty area. At roughly 34 DIP per row this shows about four rows. 150 DIP
  is an adjustable starting point, not a fixed-layout requirement; it was reduced from an initial
  300 DIP after owner feedback that even that took too much vertical space.
- Scrolling uses the list control's **existing** `ScrollViewer`
  (`ScrollViewer.VerticalScrollBarVisibility="Auto"`, already present). **No extra `ScrollViewer` is
  wrapped around it.**
- Surrounding layout checked: the list sits in `FactsPanel`, a `StackPanel` inside the Details
  vertical `ScrollViewer`, which offers unbounded height. Nothing constrains the list, so the
  `MaxHeight` is what engages the list's own scrolling rather than the page absorbing the growth.
- Keyboard navigation and the list's default selected-item scroll-into-view are untouched.
- File order, selection, classification, playback, downloads, and every subsequent Details control and
  action are unchanged, and remain reachable below the list.

No redesign, no unrelated refactor. **No test was added for the height constant**: asserting a chosen
pixel value would be a tautology rather than behaviour. This is a layout-only change, verified by the
owner at runtime (§12) and by XAML compilation, not by automation.
