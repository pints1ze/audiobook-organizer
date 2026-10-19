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
