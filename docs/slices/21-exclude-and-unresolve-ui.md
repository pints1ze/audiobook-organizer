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

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 107 passed, 0 failed | no backend change in this slice |
| Frontend tests | 125 passed, 0 failed | 9 files, 16 new. Run five times in a row, all green; the implementer had seen one existing page test fail once during the red phase, and it didn't recur |
| New tests | 4 of 4 behaviours present, passing | api: `excludeFiles` and `unresolveFiles` (200, 400, 500). `GroupFiles`: exclude and send to unresolved with their messages. `UnresolvedFiles`: each kind hides its own list option; the excluded kind's undo calls `unresolveFiles`. Page: the Excluded section with count, files and "None."; reload and focused "Excluded 1 file". Extra: the empty state stays hidden while files are excluded |
| Manual: keyboard exclude from a book | Passed | Playwright on :5173 against a scratch DB; scenario A (3 tracks), one M4B and a stray MP3, scanned with the Dashboard's "Scan incoming" button. Tab to "Files (3)", Enter, Tab Tab to "Select 01 track.mp3", Space, Tab to "Move selected to" (options: single, New book, Unresolved files, Excluded files), ArrowDown ×3, Tab, Enter. Focus landed on `role="status"` "Excluded 1 file"; the book shows "Files (2)" and "Excluded files (1)" lists `book a/01 track.mp3` |
| Manual: send a track to unresolved | Passed | By script clicks: "Select 02 track.mp3", "Unresolved files", Move. Status "Moved 1 file to unresolved files"; Unresolved lists `book a/02 track.mp3` and the stray file |
| Manual: rescan keeps both | Passed | "Scan incoming" on the Dashboard: completed with 5 unchanged; `GET /api/groups` was byte-identical before and after |
| Manual: undo from Excluded | Passed | By script clicks. The Excluded bar offers single, track, New book and Unresolved files (no "Excluded files"). Moving the track to "Unresolved files" focused "Moved 1 file to unresolved files"; Excluded shows "(0)" and "None.", and Unresolved "(3)" |
| Manual: 375 px width | Passed | With one excluded and two unresolved files and every Files list open (4 move bars), the page scroll width is 360 in a 375 viewport |

**Data:** a scratch DB and folders in the session scratchpad, not the dev `data/audiobooks.db`. The servers, the browser, the scratch data and the `.playwright-mcp/` output were removed afterwards.

## Outcome (2026-10-18, commit 7823778)

**Status:** Implemented
**Summary:** the move bar now also offers "Unresolved files" and "Excluded files", and hides whichever list the files are already in. A new "Excluded files" section, which is `UnresolvedFiles` with `kind="excluded"`, lists excluded files and sends them back (the undo) or into a book. Every move focuses the page status message. All Done when items are met, which completes phase 4's done-when line.
**Changed:** 10 code files plus 2 docs. Key ones: `components/incoming/MoveBar.tsx`, `components/incoming/UnresolvedFiles.tsx`, `pages/IncomingPage.tsx`, `services/api.ts`, `types/groups.ts`.
**Deviations:**
- `AppLayout.test.tsx` gained `excluded: []` in its fixture so it typechecks. This is outside the listed files but required by task 1. *Fine.*
- The "No books found yet." empty state now also needs `excluded` to be empty, so excluded-only libraries can still be undone. *Improvement*, with a test.
- The type is named `FileListName` instead of `FileList`, so it doesn't shadow the DOM type. *Fine.*
- Existing tests changed only to expect the new options, add `excluded` to fixtures and mocks, and pass the new required `kind` prop. No assertion was removed. *Fine.*
**Follow-ups:**
- The Unresolved section still says "These files had too little evidence to group." Files the user sends there also appear, so the wording is now partly wrong. *Minor:* reword it, for example to "These files aren't in a book yet."
- Excluded files must be skipped by the metadata and processing phases (phases 5 and 8).

