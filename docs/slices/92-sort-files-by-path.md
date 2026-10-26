# Slice 92: Sort files by path

**Phase:** 11, Parallel identification
**Goal:** in a book's file list the user puts every file in path and filename order with one button, instead of moving files one by one after combining books.

## Done when
- [x] a book's file list (Incoming, "Files (N)") has a "Sort by path" outline button above the list, shown when the book has 2 or more files
- [x] pressing it orders the files by their relative path (folders, then filename), comparing digit runs by value and ignoring case ("Part 2" before "Part 10", "disc 1/track 9" before "disc 2/track 1"), and saves that order through the existing `reorderGroup` (`PUT /api/groups/{id}/order`), which marks the book user-corrected
- [x] on success the list shows the new order, `onReordered` gets the saved group, focus stays on "Sort by path" and the polite live region announces "Sorted 5 files by path."; when the files are already in that order nothing is sent and it announces "The files are already sorted by path."
- [x] a failed save shows the existing "The new order couldn't be saved. The files have been reloaded." alert and reloads; while saving, the button is disabled like the Up/Down buttons
- [x] all four checks pass with no new warnings

## Read first
- `docs/UIUX.md` (Feedback and focus; secondary buttons are outline)
- `src/AudiobookOrganizer.Client/src/components/incoming/GroupFiles.tsx` (`move`, the `saving`/`saveFailed` state, the live region) and `GroupFiles.test.tsx` (the `services/api` mock, `file()` and `detail()` helpers, `itemNames()`)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`reorderGroup`)
- `src/AudiobookOrganizer.Client/src/utils/formatDuration.ts` and its test (the shape of a small pure helper)
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingEngine.cs` (`NaturalCompare`: the server's natural sort, which this matches in spirit)

## Tasks
- [x] **Helper.** New `utils/sortByPath.ts`: `sortByPath<T extends { relativePath: string }>(files: readonly T[]): T[]`, a new array ordered by `relativePath` with `Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })`; ties keep their current order (stable sort). Test it in `utils/sortByPath.test.ts`.
- [x] **Button.** In `components/incoming/GroupFiles.tsx` add "Sort by path" (outline, `moveButtonClass`, a lucide `ArrowDownAZ` icon `aria-hidden`) above the `<ol>` when `files.length >= 2`. Its handler computes `sortByPath(files)`; if the ids are unchanged it only sets the announcement; otherwise it saves like `move` does (`setSaving`, clear the alerts, `reorderGroup`, on success `setFiles`, announcement, `onReordered`; on failure `setSaveFailed(true)` and `load()`). Focus isn't moved (the button stays mounted). Factor the shared save path out of `move` only if it stays simple.
- [x] **Tests** (below), then all four checks.
- Data: none (uses `PUT /api/groups/{id}/order`).
- Auth: none (no auth in the app).
- Accessibility: a real `<button>` with visible text and focus ring; disabled while saving; the result is announced through the existing `aria-live="polite"` region; focus stays on the button after sorting.

## Tests to add
- `utils/sortByPath.test.ts`: numeric runs by value ("Part 2" before "Part 10"), folders before filenames ("disc 1/track 9" before "disc 2/track 1"), case ignored, and the input array is left unchanged.
- `components/incoming/GroupFiles.test.tsx`: with files loaded as `part 10`, `part 2`, `part 1`, "Sort by path" calls `reorderGroup` with the ids in `part 1, part 2, part 10` order, shows that order, calls `onReordered` and announces "Sorted 3 files by path."
- Same file: files already in path order → `reorderGroup` isn't called and "The files are already sorted by path." is announced; a book with one file shows no "Sort by path".
- Same file: a rejected `reorderGroup` shows the save alert and reloads the files.

## Manual checks
- Restart the dev API and client, combine two one-file M4B books on Incoming (move Part 2 into Part 1's book after Part 3 was added first), press "Sort by path": the parts read 1, 2, 3, the Review pane's destination lists them in that order.
- Keyboard only: Tab to "Sort by path", Enter; focus stays on the button and a screen reader announces the result.

## Out of scope
- Sorting by tag track or disc, or sorting automatically when files are moved into a book.
- A sort button in the Review pane (it has no file reordering).
- Server-side sorting: the client sends the order through the existing reorder endpoint.

## Notes
- Developer request, 2026-10-26: added to phase 11 with the developer's OK, after combining books left files in the order they were added rather than track order; path and filename usually match the track order.
- Conflict check: reordering is already a user correction (`Reorder` marks the group user-corrected), so a rescan keeps the sorted order. No new dependency (`Intl.Collator` is built in; `ArrowDownAZ` is in lucide-react).
- Files touched: `utils/sortByPath.ts` (new), `utils/sortByPath.test.ts` (new), `components/incoming/GroupFiles.tsx`, `components/incoming/GroupFiles.test.tsx`: 4.

## Test results (2026-10-26)

**Regressions:** none in this run. During implementation one full frontend run failed `ReviewPage.test.tsx > keyboard shortcuts > a approves a Ready book and moves on; Ctrl+a does not`: Failed (accepted) as a flake by the developer, 2026-10-26; ReviewPage doesn't use the changed files and it passed every later run.

| Check | Result | Notes |
|---|---|---|
| Build | Passed | API and test project 0 warnings; `-p:OutDir=<scratchpad>` |
| Typecheck | Passed | |
| Backend tests | 442 passed, 0 failed | |
| Frontend tests | 377 passed, 0 failed | 22 files, 3 full runs; 2 more after the review fix 9ada106 |
| New tests | 4 of 4 present, passing | `sortByPath.test.ts` (4); `GroupFiles.test.tsx` "Sort by path": sorts, saves, announces and keeps focus; already sorted sends nothing; no button for one file; failed save alerts and reloads |
| Manual: sort combined M4B parts on Incoming | Not run | needs the dev API and client restarted (`scripts/start.ps1`) |
| Manual: keyboard and screen reader | Not run | same |

## Outcome (2026-10-26, commit 4c39ff8)

**Status:** Implemented
**Summary:** A book's file list on Incoming has a "Sort by path" button (2 or more files) that orders the files by relative path with numeric, case-insensitive comparison (`utils/sortByPath.ts`) and saves the order through `reorderGroup`; an already sorted list sends nothing and says so. The review fix `9ada106` makes the button `aria-disabled` while saving so focus stays on it.
**Changed:** 4 code files (`utils/sortByPath.ts` and its test new, `GroupFiles.tsx`, `GroupFiles.test.tsx`)
**Done when:** all 5 met (focus after the review fix); manual checks not run.
**Deviations:**
- The button was first `disabled` while saving, which drops keyboard focus in a real browser: bug, fixed in `9ada106` with `aria-disabled` as in slice 90 (developer, 2026-10-26).
- The save path of `move` is shared as `saveOrder(ids, onSaved)`: fine, as the slice allowed.
**Follow-ups:**
- Pressing "Sort by path" again on a sorted list sets the same live-region text, so a screen reader may not announce it twice. Minor; fix if it bothers in use.
