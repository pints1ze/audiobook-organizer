# Slice 21: Exclude files, send them to unresolved and undo, from the Incoming page

**Phase:** 4, Group corrections
**Goal:** the move bar gains two destinations, "Unresolved files" and "Excluded files", so the user can take files out of any book. A new "Excluded files" section lists excluded files and can send them back (the undo). It uses the slice 20 API.

## Done when
- [x] `types/groups.ts` adds `excluded: UnresolvedFile[]` to `GroupsResponse`. `services/api.ts` adds `excludeFiles(fileIds)` (POST `/api/groups/excluded`) and `unresolveFiles(fileIds)` (POST `/api/groups/unresolved`), each mapping 200 to `{ ok: true, list }` and 400 to `{ ok: false, errors }`, and throwing otherwise
- [x] The move bar's `<select>` lists the books, then "New book", then "Unresolved files" and "Excluded files". It leaves out the list the files are already in: the Unresolved section has no "Unresolved files", and the Excluded section has no "Excluded files". A book's Files list offers both
- [x] Choosing "Unresolved files" calls `unresolveFiles`; choosing "Excluded files" calls `excludeFiles`. On success the page reloads and focuses the status message: "Moved N file(s) to unresolved files" or "Excluded N file(s)". Failures show the existing `role="alert"` and reload
- [x] Under Unresolved, an "Excluded files (N)" section explains "Excluded files stay out of books until you send them back." It lists the excluded files with checkboxes, "Select all excluded files" and the move bar, using the same component as Unresolved. It shows "None." when empty
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML; native `<select>` and checkboxes), `services/api.ts` as the only caller, the accessibility rule
- `docs/slices/20-exclude-and-unresolve-api.md` Outcome: both endpoints take `{ fileIds }` and return 200 with the full `GET /api/groups` list (now with `excluded`), or 400 keyed `fileIds`. Moving an excluded file into a book clears its state
- `docs/slices/19-assign-unresolved-files.md` Outcome: `MoveBar`, `UnresolvedFiles`, the page status message
- `src/AudiobookOrganizer.Client/src/components/incoming/MoveBar.tsx`, `UnresolvedFiles.tsx` and its test, `GroupFiles.test.tsx`, `pages/IncomingPage.tsx` and its test, `services/api.ts` (`moveFilesToGroup` is the pattern) and its test, `types/groups.ts`

## Tasks
- [x] **Types and API.** Add `excluded` to `GroupsResponse` and a `FileListResult` union (`{ ok: true; list: GroupsResponse }` or `{ ok: false; errors: { fileIds?: string[] } }`). Add `excludeFiles` and `unresolveFiles` to `services/api.ts`. Update the existing test fixtures that build a `GroupsResponse` so they typecheck. Verify: the api tests below.
- [x] **Move bar destinations.**
  - In `MoveBar.tsx`, add an optional prop `source?: 'unresolved' | 'excluded'`, which hides that list's option. Add the two options after "New book" and call the matching API, with the messages from Done when.
  - `GroupFiles` passes no `source`, so it needs no code change.
  - Update the three tests that assert the exact option list (`GroupFiles.test.tsx`, `UnresolvedFiles.test.tsx`, `IncomingPage.test.tsx`) to expect the new options. Their other assertions stay as they are.

  Verify: the component tests below.
- [x] **Excluded section.**
  - Give `UnresolvedFiles` a `kind: 'unresolved' | 'excluded'` prop. It sets the select-all label ("Select all unresolved files" or "Select all excluded files"), the select id (`unresolved-move-to` or `excluded-move-to`), the checkbox ids and the `MoveBar` `source`.
  - In `IncomingPage.tsx`, add the "Excluded files (N)" section after Unresolved, with the same handlers.

  Verify: the page test below.
- [x] **Tests.** Write the tests below. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none (uses the slice 20 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - The new options have plain text names.
  - The Excluded section has an `h2` and is a labelled `section`, like Unresolved.
  - Its checkboxes are labelled "Select {relativePath}", with unique ids per section.
  - Focus goes to the page status message after every move.
  - Errors use `role="alert"`.
  - Nothing relies on colour alone.
  - The page doesn't scroll sideways at 375 px.

## Tests to add
- `src/services/api.test.ts`: `excludeFiles` POSTs `{ fileIds }` to `/api/groups/excluded` and maps 200 and 400. `unresolveFiles` does the same for `/api/groups/unresolved`. Both throw on 500.
- `src/components/incoming/GroupFiles.test.tsx`: choosing "Excluded files" for `b.mp3` calls `excludeFiles([b])` and `onMoved("Excluded 1 file")`. Choosing "Unresolved files" calls `unresolveFiles` and `onMoved("Moved 1 file to unresolved files")`.
- `src/components/incoming/UnresolvedFiles.test.tsx`: the unresolved kind offers "Excluded files" but not "Unresolved files". The excluded kind is the reverse, labels its select-all "Select all excluded files", and its undo calls `unresolveFiles`.
- `src/pages/IncomingPage.test.tsx`: the Excluded section shows its count and files from `getGroups()`, or "None.". After excluding a file from a book, `getGroups` is called again and the focused status message reads "Excluded 1 file".

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan from the Dashboard.
  - By keyboard only, open a book's Files list, select one track and move it to "Excluded files". The focused status reads "Excluded 1 file", the book has 2 files, and Excluded lists the track.
  - Send another track from the book to "Unresolved files": it appears under Unresolved.
  - Rescan from the Dashboard and return: both stay where they were.
  - In Excluded, select the track and move it to "Unresolved files" (the undo): Excluded shows "None.".
  - At about 375 px wide, the page doesn't scroll sideways.
- Delete the scratch folders and database afterwards.

## Out of scope
- Showing excluded files in later phases (metadata and processing will skip them; phases 5 and 8).
- Dashboard counts (carried-in phase 4 item, a later slice).
- Bulk actions across sections in one step.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("exclude ... work from the UI"), the developer's exclude decision and the slice 20 API, the slice 19 shared `MoveBar` and `UnresolvedFiles`, and AGENTS.md (native controls, no new packages). No `[NEEDS CLARIFICATION]` in scope.
- **Why the move bar:** exclude and send to unresolved are moves to a list instead of to a book. Putting them in the same `<select>` keeps one control and one focus pattern, with no new buttons per file.
- **Reuse:** the Excluded section is `UnresolvedFiles` with `kind="excluded"`, not a copy (AGENTS.md section 4, rung 2).
- **Size:** 4 tasks, 9 files: `types/groups.ts`, `services/api.ts` and its test, `MoveBar.tsx`, `GroupFiles.test.tsx` (an assertion update plus the new tests), `UnresolvedFiles.tsx` and its test, `IncomingPage.tsx` and its test. That's one over the guideline, accepted because two of the test edits are one-line updates to option lists.
- **Commit:** `feat: slice 21 exclude-and-unresolve-ui`.
