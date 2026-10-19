# Slice 16: Show a group's files and reorder them on the Incoming page

**Phase:** 4, Group corrections
**Goal:** each proposed book on the Incoming page has a "Files" disclosure that lists its files in order, with Move up and Move down buttons that save the new order through `PUT /api/groups/{id}/order` (slice 15).

## Done when
- [x] Each group row has a native `<details>` "Files (N)" disclosure. Opening it loads `getGroup(id)` once and lists the files in position order: position, relative path, and disc/track when present
- [x] Each file has "Move up" and "Move down" buttons with accessible names that include the file (for example "Move 02 track.mp3 up"). Move up is disabled on the first file and Move down on the last
- [x] A move calls `reorderGroup(id, fileIds)` with the new order. On 200 the list shows the returned order and focus stays on the same button for the moved file, or on its other button if that one is now disabled. A polite live region announces "Moved {name} to position {n}". The row's "Edited" marker appears
- [x] A 400, a 404 or a thrown error shows a `role="alert"` message inside the disclosure and reloads that group's files. Buttons are disabled while a move is saving
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (`components/` grouped by feature, test beside it), Stack (plain HTML; native `<details>`, no new shadcn component), `services/api.ts` as the only caller, the accessibility rule
- `docs/slices/15-group-files-and-reorder.md` Outcome: `GET /api/groups/{id}` returns the summary fields plus `files: [{ fileId, relativePath, position, disc, track, durationMs }]`. `PUT /api/groups/{id}/order` takes `{ fileIds }` and returns 200 with the detail, 400 with `errors.fileIds`, or 404
- `docs/slices/14-edit-group-title-author.md` Outcome: the row layout, `replaceGroup` and the "Edited" marker
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` and its test, `types/groups.ts`, `services/api.ts` (`updateGroup` is the pattern) and its test

## Tasks
- [x] **Types and API.** In `types/groups.ts`, add `GroupFile` and `GroupDetail` (`GroupSummary` plus `files: GroupFile[]`), and a `ReorderGroupResult` union: `{ ok: true; group: GroupDetail }`, `{ ok: false; errors: { fileIds?: string[] } }` or `{ ok: false; notFound: true }`. In `services/api.ts`, add `getGroup(id)` (throws on a non-200) and `reorderGroup(id, fileIds)` (PUT `{ fileIds }`; maps 200, 400 and 404; throws otherwise). Verify: the api tests below.
- [x] **File list component.** Add `components/incoming/GroupFiles.tsx` with props `groupId` and `onReordered(detail)`. It loads the files on mount, renders an `<ol>` with each file's name, path, disc and track, plus the two buttons, and shows "Loading files…" while loading. Moving builds the new id order, calls `reorderGroup` and handles the outcomes and focus described in Done when. Keep a ref per button so focus can be restored after the list re-renders. The live region is a `<p aria-live="polite" className="sr-only">`. Verify: the component tests below.
- [x] **Wire into the page.** In `IncomingPage.tsx`, add the "Files ({fileCount})" `<details>` to the Evidence cell under the evidence disclosure, so the table keeps 6 columns. Mount `GroupFiles` only while it's open: track the open state with `onToggle`. `onReordered` calls the existing `replaceGroup` with the summary fields so the row shows "Edited". Verify: the page test below.
- [x] **Tests.** Write the tests below. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none (uses the slice 15 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - The disclosure is native `<details>`/`<summary>`.
  - The list is an `<ol>` so the order is announced.
  - Move buttons have unique names per file and real `disabled` states.
  - Focus stays on the moved file's control.
  - Moves are announced in a polite live region.
  - Errors use `role="alert"`.
  - Nothing relies on colour alone.
  - The list wraps on narrow screens inside the existing `overflow-x-auto` container.

## Tests to add
- `src/services/api.test.ts`: `getGroup` returns the detail on 200 and throws on 404. `reorderGroup` sends a PUT of `{ fileIds }` to `/api/groups/5/order` and returns `{ ok: true, group }` on 200, the errors on 400 and `notFound` on 404.
- `src/components/incoming/GroupFiles.test.tsx`: it lists the files in order, with Move up disabled on the first file and Move down on the last. Clicking "Move b.mp3 up" calls `reorderGroup(groupId, [b, a, c])`, shows the new order, keeps focus on that file's control and announces "Moved b.mp3 to position 1".
- `src/components/incoming/GroupFiles.test.tsx`: a rejected or `notFound` reorder shows the alert and calls `getGroup` again.
- `src/pages/IncomingPage.test.tsx`: opening "Files (3)" on a row loads `getGroup` for that id. After a successful reorder the row shows "Edited". The `services/api` mock now includes `getGroup` and `reorderGroup`.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out a scenario A folder with three tracks (copies of the fixtures), scan from the Dashboard, then open Incoming.
  - Open "Files (3)" with the keyboard and move the last file up twice using only Enter or Space. The order updates, focus stays with the file, and the row shows "Edited".
  - Rescan from the Dashboard and return to Incoming: the new order is still there.
  - At about 375 px wide the page doesn't scroll sideways.
- Delete the scratch folders and database afterwards.

## Out of scope
- Drag-and-drop reordering.
- Moving files between groups, splitting, merging and excluding (later phase 4 slices).
- Editing disc or track numbers.
- The slice 14 accessibility follow-ups: focus after a failed title save, and duplicate Edit button names.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("reorder ... work from the UI"), the slice 15 API contract, AGENTS.md (plain HTML, a feature folder under `components/`, `services/api.ts` as the only caller, no new packages) and the accessibility rule. No `[NEEDS CLARIFICATION]` in scope.
- **Why a component:** `IncomingPage.tsx` already holds the table and the edit form. The file list has its own loading, saving and focus state, so it goes in `components/incoming/` with its own test, which is the first feature folder there.
- **Size:** 4 tasks, 7 files: `types/groups.ts`, `services/api.ts` and its test, `components/incoming/GroupFiles.tsx` and its test, `pages/IncomingPage.tsx` and its test.
- **Commit:** `feat: slice 16 reorder-files-ui`.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 94 passed, 0 failed | no backend change in this slice |
| Frontend tests | 81 passed, 0 failed | 8 files, 16 new |
| New tests | 4 of 4 behaviours present, passing | api: `getGroup` (200, 404) and `reorderGroup` (200, 400, 404, 500). `GroupFiles`: ordered list with disabled end moves; move up with new order, focus and announcement; alert and reload after a 400, a 404 or a thrown error. Page: the disclosure loads only when opened; "Edited" after a reorder. Extra: path names for duplicate file names, a load-failure alert, focus kept on a still-enabled button |
| Manual: keyboard reorder | Passed | Playwright on :5173 against a scratch DB (`Database__Path`); a scenario A folder with three tracks. Focus on the "Files (3)" summary, then Enter: 3 files, with "Move 01 … up" and "Move 03 … down" disabled. Focus on "Move 03 track.mp3 up", then Enter: order 01, 03, 02, focus stays, live region "Moved 03 track.mp3 to position 2". Space: order 03, 01, 02. Focus moves to "Move 03 track.mp3 down" (up is now disabled), live region "…position 1", and the row shows "Edited" |
| Manual: order survives a rescan | Passed | "Scan incoming" on the Dashboard: completed, 3 unchanged. Incoming then lists 03, 01, 02 |
| Manual: 375 px width | Passed | Page scroll width 360 is within the 375 viewport. The table scrolls inside its container (419 vs 312) |

**Data:** a scratch DB and folders in the session scratchpad, not the dev `data/audiobooks.db`. The servers, the browser, the scratch data and the `.playwright-mcp/` output were removed afterwards.

## Outcome (2026-10-18, commit 8e6d813)

**Status:** Implemented
**Summary:** Each proposed book on the Incoming page has a native "Files (N)" disclosure. It loads the group's files only when opened and lists them in an `<ol>` with Move up and Move down buttons. A move saves through `reorderGroup`, keeps focus with the moved file and announces the move in a polite live region; the row then shows "Edited". Failures show an alert and reload the files. All Done when items are met.
**Changed:** 7 code files plus 2 docs. Key ones: `components/incoming/GroupFiles.tsx` (new, the first feature folder) and its test, `pages/IncomingPage.tsx`, `services/api.ts`, `types/groups.ts`.
**Deviations:**
- When two files in a group share a file name, their buttons and announcements use the relative path, so every name stays unique. *Improvement.*
- An alert shows when the first file load fails. *Improvement*, because the slice hadn't covered that case.
- A small `FilesDisclosure` wrapper in the page holds the open state and strips `files` before `replaceGroup`. *Fine.*
**Follow-ups:**
- Clicking Edit swaps the row for the form, which closes that row's Files list; reopening it reloads the files. *Minor.* Revisit if a group detail view replaces the inline form.
