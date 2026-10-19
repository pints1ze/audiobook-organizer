# Slice 26: Incoming page polish

**Phase:** 4, Group corrections
**Goal:** four small fixes logged during phase 4, all in the Incoming page:
- the Unresolved hint matches what the section now holds;
- focus moves to the alert after a failed title save;
- books with the same title get distinct Edit button names;
- opening Edit no longer closes that book's Files list.

## Done when
- [x] The Unresolved section's hint reads "These files aren't in a book yet." instead of "These files had too little evidence to group."
- [x] After a failed title/author save (thrown error, 404, or a 400 with no field errors), the "That book couldn't be saved…" alert gets focus. It has `tabIndex={-1}` and keeps `role="alert"`
- [x] Each Edit button's accessible name is unique. It stays "Edit {title}" (or "Edit Unknown (group N)") when no other book shares the title. When another book does share it, the name becomes "Edit {title} (group {id})"
- [x] A book's Files list stays open when its Edit form opens, and after Save or Cancel. The open state lives in the page, keyed by group id. The Files disclosure shows under the edit form while editing
- [x] All four checks pass

## Read first
- `AGENTS.md`: the accessibility rule (WCAG 2.1 AA), Stack (plain HTML; native `<details>`)
- Outcomes of `docs/slices/14-edit-group-title-author.md` (focus after a failed save, duplicate Edit names), `16-reorder-files-ui.md` (Edit closes the Files list) and `21-exclude-and-unresolve-ui.md` (the Unresolved hint)
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` (`saveFailed`, `handleFailed`, the Edit button's `aria-label`, `FilesDisclosure` and its local `open` state, the edit row) and its test

## Tasks
- [x] **Hint and failed-save focus.** Change the hint text. Give the `saveFailed` alert a ref and `tabIndex={-1}`, and focus it in an effect when `saveFailed` becomes true, as the move status message does. Verify: the page tests below.
- [x] **Unique Edit names.** Count titles across `groups`. A title that appears more than once gets " (group {id})" in its Edit button's `aria-label`. Untitled books already include their id. Verify: the page tests below.
- [x] **Files list kept open.**
  - Lift the open state out of `FilesDisclosure` into `IncomingContent`, as a `Set<number>` of open group ids. `FilesDisclosure` becomes controlled: it takes an `open` prop and calls `onToggle(open)`.
  - Render the same `FilesDisclosure` under `EditGroupForm` in the edit row.
  - Keep the `key={group.fileCount}` reload behaviour.

  Verify: the page tests below.
- [x] **Tests.** Write the tests below and update the existing hint assertion, if there is one, to the new text. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none.
- Auth: none (no authentication by decision).
- Accessibility:
  - Focus lands on the failure alert, so a keyboard or screen-reader user isn't left at the top of the page.
  - Edit buttons have unique names (WCAG 2.4.6 and 4.1.2).
  - The Files disclosure stays a native `<details>`, with its state kept across the edit.
  - Nothing relies on colour alone.
  - Nothing scrolls sideways at 375 px.

## Tests to add
- `src/pages/IncomingPage.test.tsx`: the Unresolved section shows "These files aren't in a book yet.". When `updateGroup` rejects, the "couldn't be saved" alert is focused after the reload.
- `src/pages/IncomingPage.test.tsx`: with two books titled "Dune" (ids 3 and 4) and one "Emma", the Edit buttons are named "Edit Dune (group 3)", "Edit Dune (group 4)" and "Edit Emma".
- `src/pages/IncomingPage.test.tsx`: open "Files (3)" on a book, then click its Edit. The file list is still shown under the form. After Cancel, the row's Files disclosure is still open.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan from the Dashboard.
  - By keyboard: open a book's Files list, then Edit. The list stays open under the form. Escape (cancel) leaves it open, and focus returns to Edit.
  - Rename both books to the same title. The Edit buttons read as distinct names in the accessibility tree.
  - Stop the API, then try to save a title. The alert is focused and announced.
  - At about 375 px wide, the page doesn't scroll sideways with a Files list open under the edit form.
- Delete the scratch folders and database afterwards.

## Out of scope
- Keeping the form's fields when the save fails. The form closes as it does today; the alert explains why.
- A group detail page.
- Other pages.

## Notes
- **Conflict check:** consistent with the developer's choices (2026-10-18: the Unresolved hint, Edit-form accessibility, Edit keeping the Files list open), PLAN.md phase 4 (reopened), and AGENTS.md (accessibility; plain HTML; no new packages). No `[NEEDS CLARIFICATION]` in scope.
- **Focus pattern:** the same as the page's move status message (a ref, `tabIndex={-1}`, focus in an effect), so the page uses one pattern.
- **Size:** 4 tasks, 2 files: `pages/IncomingPage.tsx` and its test.
- **Commit:** `feat: slice 26 incoming-polish`.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 117 passed, 0 failed | no backend change in this slice |
| Frontend tests | 129 passed, 0 failed | 9 files, 3 new; one existing hint assertion updated to the new text |
| New tests | 3 of 3 present, passing | the alert is focused after a thrown save and the reload; distinct Edit names for two "Dune" books and one "Emma"; the Files list stays open under the form and after Cancel |
| Manual: keyboard Edit keeps Files open | Passed | Playwright on :5173 against a scratch DB; scenario A (3 tracks), one M4B and a stray MP3, scanned with "Scan incoming". Tab to "Files (3)" and press Enter, Shift+Tab twice to "Edit track", Enter: focus in the title field, with the 3-file list open under the form. Escape: the form closes, focus is back on "Edit track", and "Files (3)" is still open with 3 files. The hint reads "These files aren't in a book yet." |
| Manual: duplicate titles | Passed | After renaming both books to "Same Title", the accessibility tree shows "Edit Same Title (group 2)" and "Edit Same Title (group 1)" |
| Manual: failed save focuses the alert | Passed | Opened Edit on group 2, merged its file into group 1 behind it through the API (group 2 then 404s), and pressed Enter to save. Focus landed on `role="alert"` with `tabindex="-1"`: "That book couldn't be saved. The list has been reloaded." |
| Manual: with the API stopped | Noted | The slice's step "stop the API, then save" leads somewhere else: the reload also fails, so the whole page shows its "The incoming groups couldn't be loaded." alert. It's announced (`role="alert"`), but focus falls back to the page body. The case the slice covers, a failed save with a working reload, passed above |
| Manual: 375 px width | Passed | Edit form open with the 4-file list under it: page scroll width 360 in a 375 viewport |

**Data:** a scratch DB and folders in the session scratchpad, not the dev `data/audiobooks.db`. The servers, the browser, the scratch data and the `.playwright-mcp/` output were removed afterwards.

## Outcome (2026-10-18, commit 76cfbd9)

**Status:** Implemented
**Summary:** four fixes, all on the Incoming page:
- the Unresolved hint reads "These files aren't in a book yet.";
- the failed-save alert takes focus after the reload;
- Edit buttons get " (group N)" when titles collide;
- which Files lists are open now lives in the page, so a book's Files list stays open under its Edit form and after Save or Cancel.

All Done when items are met. This completes the reopened phase 4 scope.
**Changed:** 2 code files plus 4 docs: `pages/IncomingPage.tsx` and its test.
**Deviations:**
- Only the thrown-error case has a focus test. The 404 and empty-400 cases share the same `handleFailed` path, and the 404 case was checked by hand. *Fine.*
- The Files list under the form reloads its files when the form opens (a new component instance). *Fine.*
**Follow-ups:**
- When the API is unreachable, a failed save also fails the reload, so the whole page swaps to its load error and focus falls back to the body. The error is announced, but focus could go to it. *Minor*: phase 9 (hardening).

