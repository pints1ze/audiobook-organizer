# Slice 14: Edit a group's title and author on the Incoming page

**Phase:** 4, Group corrections
**Goal:** on the Incoming page, the user can edit a proposed book's title and author in place, save through `PUT /api/groups/{id}` (slice 13), and see which books they've corrected.

## Done when
- [x] Each group row has an "Edit" button whose accessible name includes the book (for example "Edit The Hobbit", or "Edit Unknown" with the group's id when the title is missing)
- [x] Edit swaps that row for a form with labelled "Title" and "Author" inputs prefilled with the current values, plus Save and Cancel. Only one row is edited at a time. Focus moves to the Title input; Cancel or Escape restores the row and returns focus to its Edit button
- [x] Save calls `updateGroup`. On 200 the row shows the returned title and author and focus returns to the Edit button. On 400 the field messages show beside their inputs (`aria-invalid`, `aria-describedby`) and the form stays open. On 404 or a network error a `role="alert"` message shows and the list reloads
- [x] Rows with `userCorrected: true` show the text "Edited" next to the title
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML forms and buttons, no new shadcn component), `services/api.ts` as the only caller, the accessibility rule
- `docs/slices/13-rename-group-keep-corrections.md` Outcome: the `PUT /api/groups/{id}` contract (body `{ title, author }`; 200 returns the group with `userCorrected`; 400 is a validation problem with `errors.title` / `errors.author`; 404 for an unknown id) and the group id reuse follow-up
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` and its test, `types/groups.ts`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`saveSettings` is the pattern for a PUT with field errors) and its test
- `src/AudiobookOrganizer.Client/src/pages/SettingsPage.tsx` for how field errors, `aria-invalid` and `aria-describedby` are done today; match it

## Tasks
- [x] **Types and API.** In `types/groups.ts`, add `userCorrected: boolean` to `GroupSummary`, an `UpdateGroupRequest { title: string | null; author: string | null }`, and an `UpdateGroupResult` union: `{ ok: true; group }`, `{ ok: false; errors: { title?: string[]; author?: string[] } }` or `{ ok: false; notFound: true }`. Add `updateGroup(id, request)` to `services/api.ts`: PUT with a JSON body, mapping 200, 400 and 404 to the union and throwing on anything else. Verify: the api tests below.
- [x] **Edit form in the row.** In `IncomingPage.tsx`, keep the loaded groups in state so a saved row can be replaced. Track the id being edited. The editing row renders one `<td colSpan={6}>` holding a `<form>` with two labelled text inputs (`maxLength={500}`), Save (submit) and Cancel (`type="button"`) buttons. Escape in the form cancels. While saving, disable Save. Move focus as Done when describes; keep a ref per Edit button to restore it. Verify: the page tests below.
- [x] **Save outcomes and the "Edited" marker.** On success replace the group in state. On field errors show them under each input with `aria-invalid` and `aria-describedby`. On `notFound` or a thrown error, close the form, show the alert ("That book couldn't be saved. The list has been reloaded.") and call `getGroups()` again. Show "Edited" as small text after the title when `userCorrected` is true. Verify: the page tests below.
- [x] **Tests.** Write the tests below and update the existing `IncomingPage.test.tsx` fixtures with `userCorrected`. Verify: `npm test` and `npx tsc -b --noEmit` are green.
- Data: none (uses the slice 13 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Edit buttons have unique accessible names per book.
  - Inputs have visible `<label>`s.
  - Errors are linked with `aria-describedby` and flagged with `aria-invalid`.
  - Focus moves into the form on open and back to the Edit button on save or cancel.
  - Escape cancels.
  - The alert uses `role="alert"`.
  - "Edited" is text, not colour alone.
  - The form wraps on narrow screens inside the existing `overflow-x-auto` container.

## Tests to add
- `src/services/api.test.ts`: `updateGroup` sends a PUT with the JSON body to `/api/groups/5` and returns `{ ok: true, group }` on 200. It returns the field errors on 400 and `notFound` on 404, and it throws on 500.
- `src/pages/IncomingPage.test.tsx`: clicking "Edit The Hobbit" shows Title and Author inputs prefilled, with focus on Title. Saving new values calls `updateGroup(1, { title, author })`, and the row then shows the new values with "Edited" and focus on its Edit button.
- `src/pages/IncomingPage.test.tsx`: a 400 result shows the title message linked to the input with `aria-invalid="true"` and keeps the form open. Cancel (and, separately, Escape) restores the original values and focuses the Edit button.
- `src/pages/IncomingPage.test.tsx`: a `notFound` result shows the alert and calls `getGroups` a second time. A group with `userCorrected: true` in the first load shows "Edited".

## Manual checks
- Run the API and client against a scratch database, not the dev `data/audiobooks.db` (set `Database__Path` for the API). Lay out a scenario A folder plus one M4B (copies of the fixtures), scan from the Dashboard, then open Incoming.
  - Edit a book by keyboard only: Tab to Edit, Enter, type, Enter to save. The row updates, shows "Edited" and focus is on Edit.
  - Rescan from the Dashboard and return to Incoming: the edited title is still there.
  - Open the form and press Escape: it closes and focus returns.
  - At about 375 px wide, the open form doesn't make the page scroll sideways.
- Delete the scratch folders and database afterwards.

## Out of scope
- Merge, split, move file, exclude and reorder (later phase 4 slices).
- Clearing the "Edited" state or reverting a correction.
- Stable group ids across rescans. The page loads fresh on every visit and scans start from the Dashboard, so a stale id needs two open tabs; the slice 13 follow-up and open question stay open.
- Dashboard group and status counts.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("set title/author work from the UI"), the slice 13 API contract, AGENTS.md (plain HTML, no new packages, `services/api.ts` as the only caller) and the accessibility rule. No `[NEEDS CLARIFICATION]` in scope.
- **Id reuse:** handled here by reloading the list after a 404 or error and by the page refetching on each visit. A wrong-group rename still needs a rescan from a second tab while the form is open. That's acceptable for one user on a LAN until the open question is decided.
- **Size:** 4 tasks, 5 files: `types/groups.ts`, `services/api.ts` and its test, `IncomingPage.tsx` and its test.
- **Commit:** `feat: slice 14 edit-group-title-author`.
