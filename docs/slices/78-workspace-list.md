# Slice 78: Workspace list

**Phase:** 10, Review workspace
**Goal:** the home page `/` is the review workspace: a list of every unprocessed book with its cover, title, author and stage, filterable by stage, with the selected book in the URL and a placeholder detail pane.

## Done when
- [x] `/` shows the Review page; the Dashboard moves to `/dashboard`. The top bar reads **Review**, Dashboard, Incoming, Preview, Jobs, More.
- [x] The left column lists `GET /api/review`'s books: a 48px cover (chosen → `chosenCoverUrl`, embedded → `fileCoverUrl`, none → a placeholder with the title's initials), title, author (and narrator when set, as "Narrated by …" in muted text), runtime, and a stage chip with its text label.
- [x] Above the list, a filter row: All and each stage with its count (e.g. "Needs match 25"); the filter is in the URL (`?stage=NeedsMatch`) and the empty result says "No books in this stage."
- [x] Choosing a book sets `?book=<groupId>` (keeping `?stage`), marks it in the list (`aria-current="true"` and a visible highlight) and shows the placeholder pane: the book's 160px cover, title, author and an "Open book page" link to `/books/<groupId>`. With no book chosen the pane says "Choose a book from the list." Reloading keeps the selection.
- [ ] At 1024px and wider the list (about 360px, scrolling on its own) and the pane sit side by side; narrower, they stack, and the pane has a "Back to list" link that clears `?book`.
- [x] Loading shows a skeleton list; a failed load shows an alert with a Retry button.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Layout, Books, Accessibility), `docs/CURRENT-STATE.md`
- `docs/slices/77-review-list-api.md` (the response shape)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and its test
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getGroups`, `chosenCoverUrl`, `fileCoverUrl`) and `services/api.test.ts`
- `src/AudiobookOrganizer.Client/src/pages/JobsPage.tsx` for the existing load/error/announce pattern; `utils/formatDuration.ts`

## Tasks
- [x] Types and API: `types/review.ts` (`ReviewBook`, `ReviewStage`, `ReviewCounts`, `ReviewList`) and `getReview()` in `services/api.ts`, with an `api.test.ts` case.
- [x] `components/review/BookList.tsx`: the filter row (plain `<button>`s with `aria-pressed`), the list (`<ul>` of `Link`s to `?book=`), cover thumbnail, stage chip. Stage chip colours from Tailwind's palette at AA contrast (e.g. `bg-amber-50 text-amber-900`), always with the text label.
- [x] `pages/ReviewPage.tsx` and `ReviewPage.test.tsx`: loads the list, reads and writes `book`/`stage` with `useSearchParams`, renders `BookList` and the placeholder pane, the responsive layout and the "Back to list" link.
- [x] Routing and nav: `App.tsx` (`/` → ReviewPage, `/dashboard` → DashboardPage); `AppLayout.tsx` nav and its test. Check the Dashboard's own "Review on the Incoming page" link and any `to="/"` that meant the dashboard.
- Data: none
- Auth: none
- Accessibility: the list is a `<ul>` of links (one tab stop per book; `j`/`k` arrive in slice 81); the selected link has `aria-current="true"`; thumbnails `alt=""` (the title is beside them); the filter buttons use `aria-pressed` and their count is in the accessible name; the pane is a `<section>` labelled by the book's title heading; the skeleton is `aria-hidden` with a visually hidden "Loading books…" status; the error is `role="alert"`.

## Tests to add
- `pages/ReviewPage.test.tsx`: lists the books with title, author, runtime and stage label; the cover `img` src is the chosen-cover URL, the embedded-cover URL, or no `img` and the initials for a book without a cover.
- `ReviewPage.test.tsx`: choosing "Needs match 2" shows only those books and puts `stage=NeedsMatch` in the URL; an empty stage shows "No books in this stage."
- `ReviewPage.test.tsx`: clicking a book shows its title in the pane and marks it `aria-current`; rendering at `/?book=<id>` selects it on load; a failed load shows the alert and Retry reloads.
- `AppLayout.test.tsx`: nav has Review (`/`) and Dashboard (`/dashboard`).
- `services/api.test.ts`: `getReview` returns the parsed body on 200 and throws otherwise.

## Manual checks
- With the dev data (26 books): the list shows covers where they exist, the filter counts match `GET /api/review`, choosing a book updates the URL and pane, reload keeps it, and at 900px wide the layout stacks with "Back to list".

## Out of scope
- Anything in the detail pane beyond the placeholder: match search and accept (79), cover, metadata, destination and Approve & next (80).
- Keyboard shortcuts (81); the scan header and retiring Dashboard/Incoming/Preview from the nav (82).

## Notes
- Conflict check: matches UIUX.md (covers 48/160px, stage chips with labels, selection in the URL, stacked below 1024px). The Dashboard stays reachable until slice 82 brings scan into the workspace header.
- About 9 files touched (two are small: `App.tsx`, `types/review.ts`); one over the "about 8" guide, kept together because the route, list and page only make sense as one change.

## Test results (2026-10-25)

**Regressions:** `JobsPage` "focuses the status line again when a second book with the same title is queued" (slice 74's test) failed 2 of 12 full frontend runs ("Received element with focus:" empty); it passed in slice 74's and 75's runs. One `BookPage` test failed once in the first run (name not captured) and passed in the next 25 runs. Neither file is touched by this slice; neither is on the accepted list (gate 2).

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--artifacts-path`, dev API running) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 414 passed, 0 failed | |
| Frontend tests | 285 passed in 16 of 18 full runs | failed runs: 1× `BookPage` (unnamed), 2× `JobsPage` same-title re-focus |
| New tests | 5 of 5 present, passing | `ReviewPage.test.tsx` list/covers/initials, stage filter + URL + empty stage, selection + `?book=` on load + error/Retry; `AppLayout` Review/Dashboard; `api.test.ts` `getReview` |
| Manual: 26 dev books, counts match `GET /api/review` | Passed | All 26, Needs match 25, Needs review 1 |
| Manual: choose a book, URL and pane update, reload keeps it | Passed | `/?book=65` selects "Don't Let Him In" with its 160px cover |
| Manual: covers where they exist | **Failed** | 24 books whose embedded-cover request 404s (their scanned files are no longer at those paths; incoming changed since the last scan) show the browser's broken-image icon, not the initials placeholder |
| Manual: 1440px side by side; 900px stacked | Passed | at 900px the list comes first and the selected book's pane is below the whole list |

**Fix after testing (2026-10-25):** `BookCover` (`components/review/BookList.tsx`) falls back to the initials when its cover image fails to load, in both the list and the pane. New test: `ReviewPage.test.tsx` "shows the initials when a cover fails to load". Typecheck passed; frontend 286 of 286 in one full run. The broken-cover manual check needs a re-run against the dev data.

**Responsive layout:** the developer said the app is desktop-only in practice, so the unticked stacking/"Back to list" item isn't pursued further (stacking at 900px was checked; "Back to list" wasn't).

## Outcome (2026-10-25, commit 77d6e7d; fix 0d46a84)

**Status:** Implemented
**Summary:** `/` is now the Review workspace: a stage-filterable list of every unprocessed book (cover or initials, title, author, narrator, runtime, stage chip) beside a placeholder detail pane, with `?stage` and `?book` in the URL. The Dashboard moved to `/dashboard`. A follow-up fix shows the initials when a cover URL 404s.
**Changed:** 14 code files across both commits (key: `pages/ReviewPage.tsx`, `components/review/BookList.tsx`, `types/review.ts`, `services/api.ts`, `App.tsx`, `components/layout/AppLayout.tsx`, with tests)
**Done when:** all met except the narrow-layout item: stacking at 900px was checked, "Back to list" wasn't. The developer waived it: the app is used on desktop browsers.
**Deviations:**
- `IncomingPage`, `PreviewPage`, `NotFoundPage` (and two tests) retarget `to="/"` to `/dashboard`: fine, task 4 asked for it.
- Broken-image icon for covers whose embedded-cover request 404s (manual check): bug, fixed in `fix: slice 78` with the developer's OK; the manual re-check against the dev data hasn't been run.
- `JobsPage` same-title re-focus test (2 of 12 runs) and one unnamed `BookPage` failure (1 run): accepted by the developer as flakes.
**Follow-ups:** re-run the covers manual check with the dev data at the phase 10 verification.
