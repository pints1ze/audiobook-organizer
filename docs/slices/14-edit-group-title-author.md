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

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 90 passed, 0 failed | no backend change in this slice |
| Frontend tests | 65 passed, 0 failed | 7 files, 14 new |
| New tests | 4 of 4 behaviours present, passing | api: the four `updateGroup` cases (200, 400, 404, 500). Page: prefilled form with focus, then save, new values, "Edited" and focus back; 400 errors linked with `aria-invalid`; Cancel and Escape each restore and refocus; `notFound` and a thrown save both alert and reload; "Edited" on load. Extra: unique button names, one row at a time, an empty-400 fallback |
| Manual: keyboard edit and save | Passed | Playwright on :5173 against a scratch DB (`Database__Path`), scenario A plus one M4B. Focus on "Edit book a", then Enter: focus is on the Title input (`group-2-title`, labelled "Title", value "book a"). Typed a new title, then Enter. The row shows "Ready Player One" and "Edited", and focus is on "Edit Ready Player One" |
| Manual: Escape | Passed | Enter on Edit, then Escape: the form closes and focus returns to "Edit Ready Player One" |
| Manual: rescan keeps the edit | Passed | "Scan incoming" on the Dashboard: completed, 3 unchanged. Back on Incoming the row still shows "Ready Player One" and "Edited" |
| Manual: 375 px width | Passed | Page scroll width 375 equals the viewport. The open form (373 px) is wider than the table container (327 px), so it scrolls inside the `overflow-x-auto` container (389 vs 327). The page itself doesn't scroll sideways |

**Data:** a scratch DB and folders in the session scratchpad, not the dev `data/audiobooks.db`. The servers, the browser, the scratch data and the `.playwright-mcp/` output were removed afterwards.

## Outcome (2026-10-18, commit 93eef65)

**Status:** Implemented
**Summary:** The Incoming page now edits a book's title and author in place. Each row has an Edit button named after its book, which opens an inline form with labelled inputs, focus handling and Escape to cancel. The form saves through `updateGroup` (`PUT /api/groups/{id}`), shows 400 field errors in place, and on a 404 or error shows an alert and reloads the list. Corrected books show "Edited". All Done when items are met.
**Changed:** 5 code files plus 2 docs. They are `pages/IncomingPage.tsx` and its test, `services/api.ts` and its test, and `types/groups.ts`.
**Deviations:**
- The Edit button sits in the Title cell, after the title and "Edited", so the table keeps 6 columns. *Fine.*
- A 400 with no title or author message is handled like a 404 (alert and reload). *Fine:* it has its own test.
- Named type `GroupFieldErrors`. *Fine.*
**Follow-ups:**
- After a failed save the form closes and focus falls back to the page. The alert is announced, but a later slice could move focus to it. *Minor debt.*
- Two books with the same title get the same Edit button name. *Minor debt:* add the author or id when titles collide.
- At 375 px the open form is wider than the table container and scrolls inside it. The page itself doesn't scroll. *Fine for now.*
