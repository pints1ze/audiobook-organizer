# Slice 19: Assign unresolved files to a book

**Phase:** 4, Group corrections
**Goal:** in the "Unresolved files" section of the Incoming page, the user can select files and move them into an existing book or a new book. It uses the same move bar as a book's Files list (slice 18), pulled out into a shared component.

## Done when
- [x] The move bar (the "Move selected to" `<select>` with every target book plus "New book", the Move button, and the `moveFilesToGroup`/`createGroup` calls) lives in a new `components/incoming/MoveBar.tsx`. `GroupFiles` uses it, and all of its existing tests pass unchanged
- [x] Each unresolved file has a checkbox labelled "Select {relativePath}", and a "Select all unresolved files" checkbox shows the mixed state when only some are selected. Under the list, the move bar lists every book ("Unknown (group N)" when untitled) plus "New book". Move is disabled until a file is selected, and everything is disabled while saving
- [x] On success the page reloads `getGroups()` and shows and focuses the existing page status message ("Moved N file(s) to {title}" or "… to a new book"), and the moved files leave the Unresolved list. On a 400, a 404 or a thrown error, a `role="alert"` message shows in the Unresolved section and the page reloads
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML; native `<select>` and checkboxes, no new shadcn component), Layout (`components/` by feature, test beside it), `services/api.ts` as the only caller, the accessibility rule
- `docs/slices/18-move-merge-split-ui.md` Outcome: the move bar, `onMoveStart`/`onMoved`, the focused page status message, and the 375 px fix (the table's scroll container is `relative`)
- `docs/slices/17-move-files-and-new-group.md` Outcome: both endpoints accept unresolved file ids and take them out of `unresolved`
- `src/AudiobookOrganizer.Client/src/components/incoming/GroupFiles.tsx` and its test, `pages/IncomingPage.tsx` (the Unresolved section and `handleMoved`) and its test, `types/groups.ts` (`UnresolvedFile`)

## Tasks
- [x] **Extract `MoveBar`.** Add `components/incoming/MoveBar.tsx`, with this behaviour moved out of `GroupFiles`:
  - the destination state and fallback;
  - the labelled `<select>` and the Move button;
  - the move call and the "Moved N file(s) to …" message.

  Props:
  - `id` (for the select)
  - `targets: MoveTarget[]`
  - `fileIds: number[]`, the selected files in list order
  - `disabled: boolean`
  - `onMoveStart()`
  - `onSaving(saving: boolean)`
  - `onMoved(message)`
  - `onFailed()`

  Move `MoveTarget` there and re-export it from `GroupFiles` so existing imports keep working. `GroupFiles` keeps its selection, its alerts and its reload. Verify: `GroupFiles.test.tsx` passes unchanged.
- [x] **Unresolved list component.** Add `components/incoming/UnresolvedFiles.tsx` with props `files: UnresolvedFile[]`, `targets: MoveTarget[]`, `onMoveStart()`, `onMoved(message)` and `onFailed()`. It renders today's list (path and duration) with a checkbox per file, the "Select all unresolved files" checkbox (set `indeterminate` through a ref), a `role="alert"` when the last move failed, and `MoveBar` with the id `unresolved-move-to`. Clear the selection when `files` changes. Verify: the component tests below.
- [x] **Page wiring.** In `IncomingPage.tsx`, replace the Unresolved `<ul>` with `UnresolvedFiles`, passing every group as a target (`bookLabel`). Reuse `handleMoved` and `setMoveMessage('')` for `onMoved` and `onMoveStart`, and `onReload` for `onFailed`. Keep the "None." text when the list is empty. Verify: the page test below.
- [x] **Tests.** Write the tests below. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none (uses the slice 17 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Checkboxes and the select have visible or programmatic labels.
  - "Select all unresolved files" exposes the mixed state.
  - Move has a real `disabled` state and text.
  - Focus goes to the page status message after a move, since the moved rows disappear.
  - Errors use `role="alert"`.
  - Nothing relies on colour alone.
  - Long paths wrap (`break-all`) so the page doesn't scroll sideways at 375 px.

## Tests to add
- `src/components/incoming/UnresolvedFiles.test.tsx`: Move is disabled with nothing selected. "Select all unresolved files" ticks every file and shows mixed after one is unticked.
- `src/components/incoming/UnresolvedFiles.test.tsx`: selecting `loose.m4b` and moving it to "The Hobbit" calls `moveFilesToGroup(1, [8])` and then `onMoved("Moved 1 file to The Hobbit")`. Moving both files to "New book" calls `createGroup({ fileIds: [7, 8], title: null, author: null })`.
- `src/components/incoming/UnresolvedFiles.test.tsx`: a rejected or `notFound` move shows the alert and calls `onFailed`.
- `src/pages/IncomingPage.test.tsx`: the Unresolved move bar lists every book plus "New book". After a successful move, `getGroups` is called again, the moved file is gone from the Unresolved list, and the focused status message reads "Moved 1 file to …".

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out scenario A (three tracks), one M4B and two stray root MP3s (copies of the fixtures), and scan from the Dashboard.
  - By keyboard only, select one stray file and move it into the scenario A book. The status message is focused, the file leaves Unresolved, and the book shows 4 files.
  - Select the other stray file and move it to "New book": a new 1-file book appears, and Unresolved shows "None.".
  - Rescan from the Dashboard and return: everything stays as moved.
  - At about 375 px wide, the page doesn't scroll sideways.
  - In a book's Files list, a move still works as in slice 18.
- Delete the scratch folders and database afterwards.

## Out of scope
- Excluding files, or sending a file back to unresolved (next slice; the decision is recorded in CURRENT-STATE).
- Naming the new book in the same step. Use Edit afterwards.
- Updating a group's evidence after manual moves (slice 17 follow-up).

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("move file ... work from the UI"), the slice 17 API, which accepts unresolved ids, the slice 18 move bar and status-message focus, and AGENTS.md (no new packages, `services/api.ts` as the only caller, one component per feature in `components/incoming/`). No `[NEEDS CLARIFICATION]` in scope.
- **Why extract:** the Unresolved section needs the same select, button, API calls and messages as `GroupFiles`. Copying them would duplicate the logic (AGENTS.md section 4, rung 2), and this is the second use.
- **Unresolved across rescans:** a moved file sits in a user-corrected group, so rescans leave it there (slice 13 decision). Files still unresolved are regrouped by the rules as today.
- **Size:** 4 tasks, 6 files: `MoveBar.tsx` (new), `GroupFiles.tsx`, `UnresolvedFiles.tsx` (new) and its test, `IncomingPage.tsx` and its test.
- **Commit:** `feat: slice 19 assign-unresolved-files`.
