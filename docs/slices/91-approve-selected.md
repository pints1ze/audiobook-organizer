# Slice 91: Approve selected

**Phase:** 11, Parallel identification
**Goal:** the user approves every selected Ready book in one action, after an inline confirmation.

## Done when
- [x] the Selected books bar has "Approve selected" (outline), counting only the selected books whose stage is Ready ("Approve 3 Ready books"); with none Ready selected it is `aria-disabled` and says why ("No Ready books selected.")
- [x] pressing it shows an inline confirmation beside the bar ("Approve 3 books? They'll be copied, tagged and moved into the library.") with "Approve" and "Cancel"; focus moves to "Approve", and Cancel or `Esc` closes it and returns focus to "Approve selected"
- [x] confirming queues each selected Ready book through the existing `approveBook` (`POST /api/jobs`), one after another, then shows a `role="status"` summary ("Queued 3 books.") plus each refused book's title and message, reloads the list, drops the queued ids from the selection and remounts the pane
- [x] selected books that aren't Ready are never sent, and a thrown request error for one book is reported for that book while the rest still go
- [x] all four checks pass with no new warnings

## Read first
- `docs/UIUX.md` (Destructive or irreversible actions get an inline confirmation, not a modal; Feedback and focus)
- `docs/slices/90-accept-suggested-bar.md` and its Outcome (the bar, `aria-disabled`, pane remount)
- `src/AudiobookOrganizer.Client/src/components/review/SelectionBar.tsx`, `pages/ReviewPage.tsx` and `ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/components/names/MergeConfirm.tsx` (the inline confirmation pattern to copy)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`approveBook`) and `types/jobs.ts` (`ApproveBookResult`)

## Tasks
- [x] **Confirm.** New `components/review/ApproveConfirm.tsx` after `MergeConfirm`: the question with the count, "Approve" (filled, focused on mount) and "Cancel", `Esc` cancels; inside a `role="group"` labelled "Confirm approval".
- [x] **Bar.** In `SelectionBar.tsx` add "Approve selected" with the Ready count, taking the selected books' stages from the page (`readyIds: number[]`, in list order). It opens `ApproveConfirm`; on confirm it calls `approveBook` for each id in turn, collecting `{ groupId, ok, message }` (a thrown error becomes "The book couldn't be approved. Check the connection and try again."), then shows the summary in the bar's existing status line and calls `onApproved(results)`. While running, both action buttons are `aria-disabled`.
- [x] **Page.** In `ReviewPage.tsx` pass `readyIds` and handle `onApproved` like `onAccepted`: reload, drop queued ids from the selection, bump `paneVersion`.
- [x] **Tests** (below), then all four checks.
- Data: none (uses `POST /api/jobs`, which already refuses books whose preview isn't clean).
- Auth: none (no auth in the app).
- Accessibility: the confirmation is inline (no modal), its buttons are real buttons with visible focus; focus goes to "Approve" when it opens and back to "Approve selected" on Cancel/`Esc`; after confirming, focus returns to "Approve selected" (it stays, `aria-disabled` if nothing Ready is left) and the summary is announced through `role="status"`; the disabled reason is visible text.

## Tests to add
- `pages/ReviewPage.test.tsx`: with two Ready books and one Needs match book selected, the button reads "Approve 2 Ready books"; with only the Needs match book selected it is `aria-disabled` and "No Ready books selected." shows.
- Same file: "Approve selected" opens the confirmation with focus on "Approve"; `Esc` closes it, returns focus and calls nothing.
- Same file: confirming calls `approveBook` for the two Ready ids only (not the Needs match one), shows "Queued 1 book." and the refused book's title and message, reloads the list and keeps only the refused and non-Ready ids selected.

## Manual checks
- Restart the dev API with several Ready books: select them plus a Needs match one, Approve selected → confirm; the Ready ones show Queued, the Jobs page lists them, and the library receives their files one book at a time.
- Keyboard only: open the confirmation, Tab between Approve and Cancel, `Esc` returns focus.

## Out of scope
- A server-side batch endpoint: `POST /api/jobs` per book already validates and queues; the client calls it once per book.
- Cancelling queued jobs.
- Approving books that aren't Ready (the single-book Approve on the Preview page still covers clean books in other stages).

## Notes
- Conflict check: approval stays an explicit user action, now confirmed inline as `UIUX.md` asks for "approve a batch". Processing still runs one book at a time in `ProcessingWorker`.
- Only Ready books are sent: Ready means a clean preview (slice 77), which `POST /api/jobs` also requires; a book that changed in between is refused by the server and reported.
- With this slice every Done when line of phase 11 has an implementation; the phase's manual checks go to the developer's verification.
- Files touched: `components/review/ApproveConfirm.tsx` (new), `components/review/SelectionBar.tsx`, `pages/ReviewPage.tsx`, `pages/ReviewPage.test.tsx`: 4.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | API and test project 0 warnings; `-p:OutDir=<scratchpad>` because the dev API locks `bin/Debug` |
| Typecheck | Passed | |
| Backend tests | 437 passed, 0 failed | |
| Frontend tests | 369 passed, 0 failed | 21 files |
| New tests | 3 of 3 present, passing | `ReviewPage.test.tsx` "selection bar > approve selected", 4 tests: Ready count and disabled reason; confirmation focus and `Esc`; confirm sends only Ready ids with summary, reload and remaining selection; a thrown error reported per book |
| Manual: approve selected Ready books on the dev API | Not run | needs the dev API restarted (`scripts/start.ps1`) |
| Manual: keyboard through the confirmation | Not run | same |

## Outcome (2026-10-25, commit 22bf847)

**Status:** Implemented
**Summary:** The Selected books bar has "Approve N Ready books" (disabled with a visible reason when none are Ready), which opens an inline "Confirm approval" group; confirming queues each selected Ready book through `POST /api/jobs` in turn, reports queued and refused books, reloads the list, drops the queued ids and remounts the pane.
**Changed:** 4 code files (`components/review/ApproveConfirm.tsx` new, `SelectionBar.tsx`, `ReviewPage.tsx`, `ReviewPage.test.tsx`)
**Done when:** all 5 met; manual checks not run.
**Deviations:**
- Slice 90's accept handler and the new approve handler folded into one `afterBatch(doneIds)` in `ReviewPage.tsx`: improvement, same behaviour.
- The bar's outcome kind renamed `accepted` → `done` to share the status line: fine.
- `ApproveSelectedResult` exported from `SelectionBar.tsx`: fine.
- An extra test for the thrown-error case: improvement.
- The confirmation snapshots the Ready ids it counted: improvement.
**Follow-ups:** none.
