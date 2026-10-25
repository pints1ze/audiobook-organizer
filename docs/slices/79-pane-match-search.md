# Slice 79: Match search in the review pane

**Phase:** 10, Review workspace
**Goal:** in the Review workspace, the selected book's pane shows its chosen match and a catalog search, and picking a result saves the match and moves the book's stage in the list without leaving the page.

## Done when
- [x] The Book page's catalog search (`CatalogSearch`, `CandidateItem`, `CandidateCover`, `CandidateDetails`, `isSameCandidate`) lives in `components/book/CatalogSearch.tsx`; `BookPage.tsx` imports it and every existing `BookPage.test.tsx` test passes unchanged.
- [x] The review pane (`components/review/BookPane.tsx`) keeps slice 78's header (160px cover, title, author, narrator, stage, runtime, "Open book page") and adds a **Match** section: the chosen match (cover, title, details from `CandidateDetails`) loaded with `getMatch`, "No match chosen yet." when there is none, or an error line when the load fails; below it the catalog search, prefilled with the book's title and author.
- [x] Picking a result ("Use this match") saves it with `pickMatch`, shows it as the chosen match, announces "Match saved: <title>" in a `role="status"` line and moves focus to the Match heading; the review list then reloads in the background, so the book's chip changes from Needs match to Ready or Needs review and the filter counts update. The book stays selected even if it no longer fits the current stage filter.
- [x] Choosing another book resets the pane (no leftover results, keywords or messages from the previous book). A failed background reload keeps the current list on screen.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Actions and keyboard, Feedback and focus), `docs/CURRENT-STATE.md`
- `docs/slices/78-workspace-list.md` (the page, the pane placeholder, the URL params)
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (lines ~58–400: `BookContent`'s Chosen match section, `CatalogSearch` and its helpers) and `pages/BookPage.test.tsx` (how search and pick are mocked)
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.tsx` and `ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`searchMetadata`, `getMatch`, `pickMatch`, `getReview`) and `types/metadata.ts`

## Tasks
- [x] Move the catalog search out of `BookPage.tsx` into `components/book/CatalogSearch.tsx` (default export `CatalogSearch`; named exports `CandidateCover`, `CandidateDetails`). No behaviour or markup change; `BookPage.tsx` keeps its Chosen match section and imports the rest. Verify: `BookPage.test.tsx` passes untouched.
- [x] `components/review/BookPane.tsx`: move slice 78's `BookPane` out of `ReviewPage.tsx` and add the Match section (heading `h3` "Match" with `tabIndex={-1}`, status line, chosen match, `CatalogSearch`). It loads its own match with `getMatch(book.groupId)`, ignoring a response that arrives after the book changed. Props: `book`, `backHref`, `onMatchSaved()`.
- [x] `pages/ReviewPage.tsx`: render `<BookPane key={selected.groupId} … />` so a new book starts fresh; `onMatchSaved` re-fetches `getReview()` without showing the skeleton and, if that fetch fails, keeps the list it has.
- [x] Tests (below) in `pages/ReviewPage.test.tsx`.
- Data: none (uses the existing `GET`/`PUT /api/groups/{id}/match` and `GET /api/metadata/search`)
- Auth: none
- Accessibility: the Match section is a `<section>` labelled by its heading; after a pick, focus goes to that heading and the status line announces the save (UIUX: the section heading when the pressed button disappears); the search keeps its label, `aria-invalid`/`aria-describedby` errors, `role="alert"` failures and "N matches found." status; result covers stay `alt=""`. Only one `CatalogSearch` is on the page at a time, so its fixed `book-keywords` id stays unique.

## Tests to add
- `ReviewPage.test.tsx`: selecting a book shows "No match chosen yet." (or its stored match's title under Match) and a Keywords field prefilled with "<title> <author>".
- `ReviewPage.test.tsx`: searching lists a candidate; "Use this match" calls `pickMatch` with it, shows "Match saved: <title>", focuses the Match heading, re-fetches `getReview` and the list chip for that book now reads the new stage (e.g. Ready) and the "Needs match" count drops by one.
- `ReviewPage.test.tsx`: after picking under `?stage=NeedsMatch`, the book leaves the filtered list but its pane stays open; a failed re-fetch keeps the list and the pane.
- `ReviewPage.test.tsx`: choosing a second book after a search shows that book's prefilled keywords and no results from the first.

## Manual checks
- With the dev data: select a Needs match book, search, pick a result. The pane shows the match, the chip turns Ready or Needs review, the counts change, and reload keeps the book selected with its match shown.
- Keyboard only: Tab from the list into the pane, type keywords, Enter to search, Tab to "Use this match", Enter; focus lands on the Match heading and a screen reader (or the accessibility tree) announces "Match saved".
- Re-run slice 78's follow-up: books whose embedded cover 404s show initials, not a broken image.

## Out of scope
- Cover choice, the metadata changes, the destination and **Approve & next** (80).
- Shortcuts, including `Enter` to accept a highlighted match and `/` to focus search, and renaming "Use this match" to a filled **Accept match** button with a highlighted result (81, which decides the highlight model).
- Searching automatically when a book is selected, and stored candidates (phase 11).
- The Book page's layout; it only changes where its search code lives.

## Notes
- Conflict check: matches PLAN.md (phase 10 Next: match search and accept in the pane, 79) and UIUX.md (focus to the section heading, status beside the control, list and pane load separately). The search runs on demand, not on selection, so `j`/`k` (81) won't hit the Audible API per keypress; background search is phase 11.
- "Use this match" stays an outline button for now so `BookPage.test.tsx` doesn't change; UIUX's filled "Accept match" lands with the highlight in 81.
- The pane's own `getMatch` duplicates a few lines of `BookContent`'s; extracting a shared Chosen match component isn't worth it for two callers with different headings and layouts.
- Files: `components/book/CatalogSearch.tsx` (new), `pages/BookPage.tsx`, `components/review/BookPane.tsx` (new), `pages/ReviewPage.tsx`, `pages/ReviewPage.test.tsx`. 5 files, 4 tasks, under the cap.
