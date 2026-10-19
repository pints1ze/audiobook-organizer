# Slice 18: Move, merge and split from a book's file list

**Phase:** 4, Group corrections
**Goal:** inside a book's "Files" list on the Incoming page, the user can select files and move them to another book or to a new book. Moving some files covers move file and split; selecting all of them covers merge. It uses the slice 17 API.

## Done when
- [x] Each file in `GroupFiles` has a checkbox labelled "Select {name}" (the same names as the move buttons). A "Select all files" checkbox toggles them all and shows the mixed state when only some are selected
- [x] Under the list, a "Move selected to" `<select>` lists every other book by title ("Unknown (group N)" when untitled) plus "New book", with a "Move" button. The button is disabled until at least one file is selected
- [x] Choosing a book calls `moveFilesToGroup(targetId, fileIds)`; choosing "New book" calls `createGroup({ fileIds, title: null, author: null })`. Selected files are sent in their list order. Both controls are disabled while saving
- [x] On success the page reloads `getGroups()` and shows and focuses a status message (`role="status"`, `tabIndex={-1}`): "Moved N file(s) to {title}", or "Moved N file(s) to a new book". A book left empty disappears from the list. On a 400, a 404 or a thrown error, a `role="alert"` message shows inside the file list and the files reload
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML; a native `<select>` is plain HTML, so no shadcn select), Layout, `services/api.ts` as the only caller, the accessibility rule
- `docs/slices/17-move-files-and-new-group.md` Outcome: `POST /api/groups/{id}/files` with `{ fileIds }` returns 200 with the detail, 400 with `errors.fileIds`, or 404. `POST /api/groups` with `{ fileIds, title, author }` returns 201 with the detail, or 400
- `docs/slices/16-reorder-files-ui.md` Outcome: `GroupFiles`, `displayNames`, the `FilesDisclosure` wrapper
- `src/AudiobookOrganizer.Client/src/components/incoming/GroupFiles.tsx` and its test, `pages/IncomingPage.tsx` and its test, `services/api.ts` (`reorderGroup` is the pattern) and its test, `types/groups.ts`

## Tasks
- [x] **Types and API.** In `types/groups.ts`, add `MoveFilesResult`, with the same union shape as `ReorderGroupResult`. In `services/api.ts`, add `moveFilesToGroup(id, fileIds)` (POST `/api/groups/{id}/files`, mapping 200, 400 and 404) and `createGroup(request)` (POST `/api/groups`, mapping 201 and 400). Both throw on any other status. Verify: the api tests below.
- [x] **Selection and move bar in `GroupFiles`.** Add new props:
  - `targets: { id: number; label: string }[]`: the other books.
  - `onMoved(message: string)`.

  Add the checkboxes, the "Select all files" checkbox (set `indeterminate` through a ref) and, under the `<ol>`, a labelled `<select>` and the Move button. Keep the selection in state as a set of file ids, cleared after a reload. Call the API as in Done when. On success call `onMoved` with the message; on failure show the alert and reload. Verify: the component tests below.
- [x] **Page wiring.** In `IncomingPage.tsx`, pass each `FilesDisclosure` the other groups as `targets` (label: the title, or "Unknown (group N)"). Pass `onMoved`, which reloads `getGroups()`, then shows the status message above the table and focuses it after the reload. Clear the message when an edit or another move starts. Verify: the page test below.
- [x] **Tests.** Write the tests below. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none (uses the slice 17 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Checkboxes and the select have visible labels.
  - "Select all files" exposes the mixed state.
  - The Move button has a real `disabled` state and text, not just an icon.
  - After a move, focus goes to the status message, because the row it came from may be gone.
  - Errors use `role="alert"`.
  - Nothing relies on colour alone.
  - The bar wraps on narrow screens inside the existing `overflow-x-auto` container.

## Tests to add
- `src/services/api.test.ts`: `moveFilesToGroup` POSTs `{ fileIds }` to `/api/groups/5/files` and maps 200, 400 and 404. `createGroup` POSTs the body to `/api/groups` and maps 201 and 400. Both throw on 500.
- `src/components/incoming/GroupFiles.test.tsx`: Move is disabled with nothing selected. "Select all files" selects every file and shows mixed after one is unticked. Selecting `b.mp3` and moving to "Other book" calls `moveFilesToGroup(otherId, [b])` and then `onMoved("Moved 1 file to Other book")`.
- `src/components/incoming/GroupFiles.test.tsx`: moving two files to "New book" calls `createGroup({ fileIds: [a, c], title: null, author: null })` with the files in list order. A rejected or `notFound` move shows the alert and calls `getGroup` again.
- `src/pages/IncomingPage.test.tsx`: the target list for a row holds the other books, not the row's own. After a successful move, `getGroups` is called again and the focused status message reads "Moved 1 file to …".

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan from the Dashboard.
  - By keyboard only, select one scenario A track and move it to "New book". The status message is focused, and a new book with 1 file appears.
  - Select all files of the M4B book and move them into the scenario A book. The M4B book disappears.
  - Rescan from the Dashboard and return: everything stays as moved.
  - At about 375 px wide, the page doesn't scroll sideways.
- Delete the scratch folders and database afterwards.

## Out of scope
- Assigning unresolved files to a book from the Unresolved section (next slice; it reuses the move bar).
- Naming the new book in the same step. Use Edit afterwards.
- Excluding files (open question).
- Updating a group's evidence after manual moves (slice 17 follow-up).

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("merge, split, move file ... work from the UI"), the slice 17 API contract and its decision that merge is "move all files" and split is "create from files", and AGENTS.md (native `<select>` and checkboxes, no new packages, `services/api.ts` as the only caller). No `[NEEDS CLARIFICATION]` in scope.
- **Focus choice:** a move can delete the source book and re-sort the list, so focus goes to a page-level status message rather than to a row that may no longer exist.
- **Split:** unresolved-file assignment is the next slice. Including it would mean extracting the move bar into its own component and touching the unresolved section, which comes to about 9 files.
- **Size:** 4 tasks, 7 files: `types/groups.ts`, `services/api.ts` and its test, `components/incoming/GroupFiles.tsx` and its test, `pages/IncomingPage.tsx` and its test.
- **Commit:** `feat: slice 18 move-merge-split-ui`.
