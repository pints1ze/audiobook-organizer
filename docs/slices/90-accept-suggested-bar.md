# Slice 90: Accept suggested bar

**Phase:** 11, Parallel identification
**Goal:** in the Review list the user selects several books and accepts their suggested matches in one action.

## Done when
- [x] each book row in the Review list has a checkbox ("Select <title>") beside its link, and a row with a `suggestedScore` and stage Needs match shows "Suggestion 87" in its meta line
- [x] above the list, a selection bar shows "N selected", a "Select all shown" checkbox (checked, unchecked or mixed for the shown list), "Accept suggested match" (disabled with none selected) and "Clear selection"
- [x] "Accept suggested match" calls `POST /api/review/accept-suggested` with the selected ids, then shows a `role="status"` summary ("Accepted 3 suggested matches.") plus each skipped book's title and error, reloads the list, drops the accepted ids from the selection, and remounts the pane so the selected book shows its new match
- [x] a request failure shows a `role="alert"` beside the bar and keeps the selection; the selection keeps only ids still in the list after a reload
- [x] the slice 88 xUnit2013 warning (`CandidateSearchWorkerTests.cs:130`) is gone; all four checks pass with no new warnings in either project

## Read first
- `docs/UIUX.md` (Actions and keyboard, Feedback and focus, Accessibility)
- `docs/slices/89-accept-suggested-api.md` (the endpoint and its errors)
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.tsx` and `ReviewPage.test.tsx` (state, reload, `j`/`k`, the `services/api` mock)
- `src/AudiobookOrganizer.Client/src/components/review/BookList.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getReview`, `pickMatch` for response handling) and `types/review.ts`
- `src/AudiobookOrganizer.Client/src/components/incoming/MoveBar.tsx` (an existing selection-bar pattern)

## Tasks
- [x] **Types and API.** `types/review.ts`: add `suggestedScore: number | null` to `ReviewBook` (and to the fixtures in `ReviewPage.test.tsx`), plus `AcceptSuggestedResult { groupId: number; accepted: boolean; error: string | null }`. `services/api.ts`: `acceptSuggested(groupIds): Promise<AcceptSuggestedResult[]>` (200 → `results`; 400 → throw with the first `groupIds` error; other statuses throw).
- [x] **List.** `components/review/BookList.tsx`: take `selectedIds: ReadonlySet<number>` and `onToggle(groupId)`; each `<li>` becomes a flex row with a checkbox (a real `<input type="checkbox">` with an `aria-label` "Select <title>") before the existing `Link`; add "Suggestion N" to the meta line for Needs match books with a score. `j`/`k` keep focusing the link.
- [x] **Bar.** New `components/review/SelectionBar.tsx`: count, "Select all shown" checkbox (`indeterminate` set through a ref when some are selected), "Accept suggested match" (outline button; disabled while running or with none selected), "Clear selection", and the status/alert lines. It calls `acceptSuggested` and reports results to the page through `onAccepted(results)`.
- [x] **Page.** `pages/ReviewPage.tsx`: hold the selection (`Set<number>`), prune it to listed ids after each load, render `SelectionBar` above `BookList`, and on `onAccepted` reload the list, remove accepted ids from the selection and bump a `paneVersion` used in `BookPane`'s key. Titles for skipped books come from the loaded list. Fix the xUnit2013 warning in `src/AudiobookOrganizer.Api.Tests/Services/CandidateSearchWorkerTests.cs:130` with `Assert.Single`.
- [x] **Tests** (below), then all four checks.
- Data: none.
- Auth: none (no auth in the app).
- Accessibility: each checkbox has a visible focus ring and an accessible name with the book title; the bar is a `role="group"` with `aria-label="Selected books"`; "Select all shown" exposes the mixed state (`indeterminate`); the summary is `role="status"` and a failure `role="alert"`, both beside the bar; focus stays on "Accept suggested match" after it runs (the button stays). The list gains one tab stop per book (the checkbox), recorded in Notes.

## Tests to add
- `pages/ReviewPage.test.tsx`: checking two books' checkboxes shows "2 selected"; "Select all shown" under a stage filter selects only the shown books and shows the mixed state after one is unchecked.
- Same file: "Accept suggested match" calls `acceptSuggested` with the selected ids, shows "Accepted 1 suggested match." and the skipped book's title with its error, reloads the list and keeps only the skipped id selected.
- Same file: a rejected `acceptSuggested` shows an alert and keeps the selection; a Needs match book with `suggestedScore: 87` shows "Suggestion 87".

## Manual checks
- Restart the dev API with background-searched books: select three Needs match books, press "Accept suggested match"; they move to Needs review or Ready, the summary names any skipped book, and the selected book's pane shows its new match.
- Keyboard only: Tab moves checkbox → link → next checkbox; Space toggles a checkbox; `j`/`k` still move between links.

## Out of scope
- Batch approve of selected Ready books (next slice; it reuses this bar).
- Shift-click range selection and a keyboard shortcut for selecting.
- Undoing a batch accept.

## Notes
- Conflict check: accepting runs only when the user presses the button for books they checked, so "never applied without a click" holds. Batch accept isn't destructive (each match can be changed again), so it gets no confirmation; UIUX's inline confirmation is for batch approve.
- Slice 83 decided "one tab stop per book" for the list. A checkbox per row adds a second; that's the usual pattern for selectable lists, and `j`/`k` still give fast movement between books. Recorded as a deliberate change to that decision.
- Files touched: `types/review.ts`, `services/api.ts`, `components/review/BookList.tsx`, `components/review/SelectionBar.tsx` (new), `pages/ReviewPage.tsx`, `pages/ReviewPage.test.tsx`, `CandidateSearchWorkerTests.cs`: 7.
- Later slice for this phase: select Ready books and approve them in one action (an "Approve selected" button on this bar with an inline confirmation).

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | API and test project 0 warnings (the slice 88 xUnit2013 warning is gone); `-p:OutDir=<scratchpad>` because the dev API locks `bin/Debug` |
| Typecheck | Passed | |
| Backend tests | 437 passed, 0 failed | |
| Frontend tests | 365 passed, 0 failed | 21 files |
| New tests | 3 of 3 present, passing | `ReviewPage.test.tsx` "selection bar" block, 6 tests: count and select all shown with mixed state; accept summary, skipped book, reload and remaining selection; failure alert keeps selection; "Suggestion 87"; plus pruning after a reload |
| Manual: batch accept three background-searched books on the dev API | Not run | needs the dev API restarted (`scripts/start.ps1`) |
| Manual: keyboard Tab/Space/`j`/`k` | Not run | same |

## Outcome (2026-10-25, commit c203386)

**Status:** Implemented
**Summary:** Review rows have a "Select <title>" checkbox and Needs match books show "Suggestion N". A Selected books bar above the list has Select all shown (with a mixed state), the count, Accept suggested match and Clear selection; accepting posts the selected ids, shows the summary and each skipped book's error, reloads, keeps only unaccepted ids and remounts the pane.
**Changed:** 7 code files (`components/review/SelectionBar.tsx` new, `BookList.tsx`, `ReviewPage.tsx`, `ReviewPage.test.tsx`, `services/api.ts`, `types/review.ts`, `CandidateSearchWorkerTests.cs`)
**Done when:** all 5 met; manual checks not run.
**Deviations:**
- Accept suggested match uses `aria-disabled` instead of `disabled`, keeping focus on it while it runs: improvement, as `ScanControl` does.
- The pane remounts after the reload finishes, so it gets the new stage too: improvement.
- An extra test for pruning the selection after a reload: improvement.
- The selection is passed to the bar as an ordered array, so requests follow list order: fine.
- The list now has two tab stops per book (checkbox and link), changing slice 83's one-per-book decision as the slice planned: fine, recorded in Decisions.
**Follow-ups:** batch approve reuses this bar (next slice).
