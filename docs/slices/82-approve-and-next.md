# Slice 82: Approve & next

**Phase:** 10, Review workspace
**Goal:** a Ready book is approved from the review pane with one filled **Approve & next** button, which queues it and moves straight to the next book in the list.

## Done when
- [ ] Under the pane's header (title, author, stage, runtime; above Match), an action line shows one thing per stage:
  - **Ready:** a filled **Approve & next** button (the pane's only filled button).
  - **Needs review:** "Fix the problems under Destination to approve."
  - **Needs match:** "Pick a match or enter the metadata to approve."
  - **Queued:** "Queued for processing." with a link to Jobs.
  - **Failed:** "Processing failed." with a link to Jobs.
- [x] Pressing **Approve & next** calls `approveBook`. On success the selection moves to the next book in the list as currently shown (with the stage filter applied): the one after the approved book, else the one before it, else none. A page-level `role="status"` line reads "Queued <title>." The list reloads in the background, so the approved book's chip reads Queued and the counts update. Focus goes to the next book's title heading. With no next book, the pane says "Choose a book from the list." and focus goes to the status line.
- [x] While the request runs, the button reads "Approving…" and is disabled. A refusal (400/409/404 message) or a network failure shows a `role="alert"` beside the button, and the book stays selected.
- [x] One-file books' Destination summary reads "Show the file", not "Show all 1 files" (slice 81 follow-up).
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Actions and keyboard, Feedback and focus), `docs/CURRENT-STATE.md`
- `docs/slices/81-pane-destination.md` (Outcome)
- `src/AudiobookOrganizer.Client/src/components/review/BookPane.tsx`, `components/review/BookList.tsx` (the stage filter: `stage ? books.filter(...) : books`), `components/review/Destination.tsx`
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.tsx` and `ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/pages/PreviewPage.tsx` (`ApproveBook`: the approve call and its refused/queued states) and `services/api.ts` (`approveBook`)

## Tasks
- [x] `components/review/ApproveAction.tsx` (`book`, `onApproved()`): the per-stage line above; the button calls `approveBook(book.groupId)`, shows "Approving…" and the alert; on success calls `onApproved()`.
- [x] `components/review/BookPane.tsx`: render `ApproveAction` under the header; give the title `h2` `tabIndex={-1}` and a `focusTitle` prop that focuses it on mount.
- [x] `pages/ReviewPage.tsx`: `handleApproved(book)` works out the next book from the shown list *before* reloading, sets `?book=` (keeping `?stage`), sets the status message, reloads the list with `reload()`, and focuses the next pane's title (`focusTitle`) or the status line. The status line sits above the two columns and is cleared when the user chooses another book.
- [x] `components/review/Destination.tsx`: "Show the file" for one file. Tests (below).
- Data: none (existing `POST /api/jobs`)
- Auth: none
- Accessibility: the button's accessible name is "Approve & next" with `aria-describedby` pointing at the book's title heading. The status line is `role="status"` and the alert is `role="alert"`. Focus moves to the next book's title heading after approval (UIUX: "the next book after Approve & next"), or to the status line (`tabIndex={-1}`) when no book is left. The Jobs links are real links with visible text.

## Tests to add
- `pages/ReviewPage.test.tsx`: a Ready book shows the filled **Approve & next** button; Needs review, Needs match, Queued and Failed books show their line and no button.
- `ReviewPage.test.tsx`: Approve & next on the first of three books calls `approveBook` with its id, selects the second (URL `book=` and pane title), announces "Queued <title>.", focuses the second book's title heading and re-fetches `getReview`.
- `ReviewPage.test.tsx`: under `?stage=Ready`, approving the last shown book selects the one before it; approving the only shown book clears the selection and focuses the status line.
- `ReviewPage.test.tsx`: a refused approval shows its message as an alert and keeps the book selected; "Approving…" is shown while pending.
- `ReviewPage.test.tsx` (or Destination's case): a one-file book's summary reads "Show the file".

## Manual checks
- With the dev data: make a book Ready (book 236 "Shroud" already is) and press **Approve & next** with the keyboard. The next book opens with focus on its title, "Queued Shroud." is announced, Shroud's chip reads Queued, and the Jobs page shows the job running and finishing. This processes a real dev book: with keep-source off, its incoming files are deleted after copying (slice 57). Ask the developer before running it, or use a disposable book.
- Approve a Needs review book from the API with `curl -X POST /api/jobs` to confirm the refusal message reads well in the pane (or simulate it in the test).

## Out of scope
- The `a` shortcut and the `?` help popover (83); scan in the header and retiring Dashboard/Incoming/Preview from the nav (84).
- Batch approve (phase 11).
- Undoing an approval: a queued job is run by the worker; cancelling isn't in the app.

## Notes
- Conflict check: matches UIUX.md: one filled primary action per pane state, focus to the next book after Approve & next, `role="status"`/`role="alert"` beside the control. `POST /api/jobs` still refuses a book whose preview isn't clean (slice 54), so a stale Ready chip can't queue a bad book; the alert covers it.
- "Next" is taken from the shown list before the reload, so the approved book (now Queued) dropping out of a `Ready` filter doesn't change which book comes next.
- Files: `components/review/ApproveAction.tsx` (new), `BookPane.tsx`, `Destination.tsx`, `pages/ReviewPage.tsx`, `pages/ReviewPage.test.tsx`. 5 files, 4 tasks.

**Fix (2026-10-25, developer's choice "Outline them"):** Search (`CatalogSearch`), Upload (`CoverPicker`) and Save (`MetadataEditor`) are outline buttons, on the pane and the Book page, so Approve & next is the pane's only filled button (UIUX: others are outline or ghost). New test, seen failing first with "Search", "Upload", "Save": `ReviewPage.test.tsx` "is the only filled button in the pane, also while the metadata is being edited". Commit 1a3bf42.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--artifacts-path`; the dev API locks `bin/`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 415 passed, 0 failed | |
| Frontend tests | 321 passed, 0 failed | 19 files; 3 full runs after the fix, all green |
| New tests | 5 of 5 present, passing | 12 new cases in `ReviewPage.test.tsx` (11 in the feat commit, 1 in the fix): per-stage lines, approve → next/announce/focus/re-fetch, last/only book under `?stage=Ready`, refusal + "Approving…", "Show the file", only filled button |
| Manual: keyboard Approve & next on Shroud (236), developer approved | Passed | Enter on the focused button: URL → `?book=340`, focus on `h2` "Sigra's Roost (Unabridged)", status "Queued Shroud.", Shroud's chip Queued, counts Ready 1 → 0, Queued 0 → 1; the job reached `Succeeded` and Shroud left the list. The pane's only filled button was Approve & next; one-file summary read "Show the file" |
| Manual: refusal message from the real API | Passed | `POST /api/jobs {groupId: 65}` → 400, detail "A file is already at Lisa Jewell/Don't Let Him In/001. Don't Let Him In (Lisa Jewell).m4b." — `approveBook` shows the detail; nothing queued. Not clicked in the pane (65 is Needs review, so it has no button) |

**Dev data changed:** Shroud (236) was processed into the library (`Y:\audiobooks\Adrian Tchaikovsky\Shroud`) and, per the keep-source setting, its source handled by the worker.

## Outcome (2026-10-25, commit 1ebee42; fix 1a3bf42)

**Status:** Implemented
**Summary:** The review pane has a per-stage action line under its header. For Ready books it is the filled **Approve & next** button, which queues the book, moves to the next book in the shown list, announces it and focuses the next title. The Destination summary reads "Show the file" for one-file books, and the shared Search, Upload and Save buttons are now outline buttons.
**Changed:** 5 code files in the feat commit (`components/review/ApproveAction.tsx` new, `BookPane.tsx`, `Destination.tsx`, `pages/ReviewPage.tsx`, `ReviewPage.test.tsx`); 4 in the fix (`CatalogSearch.tsx`, `CoverPicker.tsx`, `MetadataEditor.tsx`, `ReviewPage.test.tsx`)
**Done when:** all 5 met (item 1's "only filled button" by the fix).
**Deviations:**
- Search, Upload and Save restyled outside the slice's files, also on the Book page: fine, the developer chose it.
- `ApproveAction` also takes `headingId` for `aria-describedby`: fine.
- The page status line renders only once the list has loaded, and remembers both the approved and the next book so the URL change doesn't clear it: fine, implementation detail.
**Follow-ups:** none new.
