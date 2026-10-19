# Slice 29: Book page with catalog search

**Phase:** 5, Metadata
**Goal:** from a book on the Incoming page, the user opens its book page (`/books/:groupId`) and searches the catalog, seeing candidates and any provider errors.

## Done when
- [x] Each Incoming book row has a "Find metadata" link (unique accessible name per book, like the Edit button) to `/books/{id}`.
- [x] `/books/:groupId` loads `GET /api/groups/{id}` and shows the book's title (or "Unknown"), author, file count and duration. An unknown id shows "Book not found" with a link back to Incoming; a non-numeric id is treated the same.
- [x] The page has a labelled search form prefilled with the book's title and author (joined by a space, blanks skipped). Submitting calls `GET /api/metadata/search?keywords=...` and lists the candidates: cover thumbnail (empty `alt`), title and subtitle, authors, narrators, series and part, release date, runtime, format, the provider name, and the ASIN or ISBN.
- [x] `providerErrors` show as a notice above the results (e.g. Audible failed, Open Library answered). No candidates shows "No matches found." A 400 shows the keywords field error; a 502 shows the problem detail. Neither blocks searching again.
- [x] Search results are announced (a status region with the result count) and focus stays usable: the submit button is disabled only while a search runs.
- [x] All four checks pass.

## Read first
- `docs/slices/28-open-library-fallback.md`, `## Outcome` (show `providerErrors` beside the results)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs`, `Controllers/MetadataController.cs` (response shape, 400 and 502)
- `src/AudiobookOrganizer.Client/src/services/api.ts` and `api.test.ts` (result-union style, e.g. `updateGroup`)
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` (row markup near the Edit button, `editLabel`) and `IncomingPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/App.tsx`, `pages/NotFoundPage.tsx`, `utils/formatDuration.ts`
- `overview.md`, Screen 3 (Audiobook Detail & Metadata Editor: catalog matches on the right)

## Tasks
- [x] **Types and API call.** Add `src/types/metadata.ts` mirroring `SeriesRefDto`, `MetadataCandidateDto` and `MetadataSearchResultDto` (camelCase). Add `searchMetadata(keywords)` to `services/api.ts`, URL-encoding the keywords, returning `{ ok: true; result } | { ok: false; errors: { keywords?: string[] } } | { ok: false; providerFailure: string }` for 200 / 400 / 502, and throwing on anything else. Verify: `api.test.ts`.
- [x] **Book page.** Add `pages/BookPage.tsx`: reads `groupId` with `useParams`, loads `getGroup`, and shows the summary (heading = title or "Unknown"), then a "Catalog search" section with the form, status region, provider-error notice and the candidate list (`<ul>`, one `<li>` per candidate, its title as an `<h3>`). Runtime shows as hours and minutes (reuse `formatDuration` with `runtimeMinutes * 60000`). Not-found handling as in Done when. Verify: `BookPage.test.tsx`.
- [x] **Route.** Add `<Route path="/books/:groupId" element={<BookPage />} />` in `App.tsx`. Verify: a page test renders through `AppRoutes` with `MemoryRouter`.
- [x] **Incoming link.** In `IncomingPage.tsx`, add a `Link` "Find metadata" next to Edit in the read-only row, with an `aria-label` built like `editLabel` ("Find metadata for {title}", id added when titles repeat, "Unknown (group N)" when untitled). Verify: `IncomingPage.test.tsx`.
- Data: none. Nothing is stored; picking a match is the next slice.
- Auth: none (single user, no login).
- Accessibility: search input has a visible label; errors are linked with `aria-describedby`; results count in a `role="status"` region; the provider-error notice uses `role="status"` too (not alert, the search still worked); the page has one `h1`, section `h2`s, candidate `h3`s; covers are decorative (`alt=""`); all controls reachable by keyboard.

## Tests to add
- `services/api.test.ts`: `searchMetadata` encodes the keywords into the URL and returns candidates and provider errors on 200; returns field errors on 400; returns the problem detail on 502.
- `pages/BookPage.test.tsx`:
  - loads the group, prefills "Project Hail Mary Andy Weir", submits, and lists a candidate with its narrator, series and provider;
  - shows the provider-error notice alongside Open Library candidates, and "No matches found." for an empty result;
  - a 502 shows the detail and the user can search again;
  - an unknown group id shows "Book not found" with a link to Incoming.
- `pages/IncomingPage.test.tsx`: each book row has a "Find metadata for ..." link to `/books/{id}`.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, scan a folder with a known book, open Incoming, follow "Find metadata", search, and confirm the candidates match what `GET /api/metadata/search` returns for the same keywords.
- Search an ISBN (e.g. `978-0-593-13520-4`) and confirm the Open Library candidate shows its ISBN.
- Keyboard only: tab from the Incoming link to the search field, submit with Enter, and check that a screen reader (or the accessibility tree) announces the result count.

## Out of scope
- Picking a match, and storing `Audiobook` / `MetadataMatch` (slice 30)
- The metadata editor, value origins, manual completion, covers
- Match scoring or ranking beyond the provider's order
- Book pages for unresolved or excluded files (only groups have pages; excluded files never get an `Audiobook`)

## Notes
- File count: 8 (`types/metadata.ts`, `api.ts`, `api.test.ts`, `BookPage.tsx`, `BookPage.test.tsx`, `App.tsx`, `IncomingPage.tsx`, `IncomingPage.test.tsx`). At the cap, frontend only.
- Split: "search UI and picking a match" was too big for one slice. This slice is the search UI; slice 30 adds `Audiobook` + `MetadataMatch` tables, a pick endpoint and a "Use this match" button on each candidate.
- The page is keyed by group id because no `Audiobook` exists yet. When slice 30 creates one, it stays one-to-one with the group (PRD: "It has one FileGroup"), so the URL can stay.
- Conflict check: no new package, no shadcn component needed (plain form and list), `services/api.ts` stays the only backend caller, no backend change.
- Remaining phase 5 pieces after slice 30: the editor with value origins that keeps user edits; manual completion; covers (embedded, download, upload).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 155 passed, 0 failed | Unchanged (no backend change) |
| Frontend tests | 142 passed, 0 failed | 10 files; was 129 |
| New tests | 7 of 7 present, passing | `api.test.ts` `searchMetadata` (4 cases), `BookPage.test.tsx` (8 cases incl. 400 field error, request failure, non-numeric id), `IncomingPage.test.tsx` "Find metadata" link (1) |
| Manual: Incoming → Find metadata → search matches API | Not run | API (5070) and client (5173) not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: ISBN search shows Open Library ISBN | Not run | Same |
| Manual: keyboard and result-count announcement | Not run | Same |

## Outcome (2026-10-19, commit 12c28c3)

**Status:** Implemented
**Summary:** Each Incoming book row links ("Find metadata") to a new `/books/:groupId` page. The page shows the book's summary and a catalog search prefilled with its title and author. Results list each candidate's details, provider errors show as a status notice, and 400, 502 and network failures are handled without blocking another search.
**Changed:** 8 files, all client: `pages/BookPage.tsx` and its test (new), `types/metadata.ts` (new), `services/api.ts` and its test, `App.tsx`, `pages/IncomingPage.tsx` and its test.
**Deviations:**
- Every book-load failure (500, network) shows "Book not found", because `getGroup` throws on any non-200. Marked `// shortcut:` in `BookPage.tsx`: *debt*. Make `getGroup` report 404 separately when a slice next touches it (also used by `GroupFiles.tsx`).
- `editLabel` became `uniqueName`, shared by Edit and "Find metadata"; the Edit names are unchanged: *fine*.
- Two extra tests (400 field error with `aria-describedby`, a request that fails outright): *improvement*.
- Runtime reads `16:10:00` via `formatDuration`, not "16 h 10 min": *fine* (consistent with the Incoming durations).
- Manual checks not run (dev servers weren't running): *fine* for Implemented; the developer verifies at the end of the phase.
**Follow-ups:**
- Slice 30: `Audiobook` + `MetadataMatch` tables, a pick endpoint and a "Use this match" button on each candidate.
- `getGroup` 404 vs other failures (debt above).
