# Slice 43: Merge on names page

**Phase:** 6, Normalization
**Goal:** on the People and Genres pages the user can see suggested duplicates and merge any entry into another, always with an explicit confirmation.

## Done when
- [x] Each page shows a "Possible duplicates" section from `GET /api/{kind}/suggestions`: one item per suggestion with its `reason` and a "Merge {merge.name} into {keep.name}" button. With no suggestions the section says "No possible duplicates." A failed suggestions load shows an alert inside the section only; the list still works.
- [x] Each entry row has a "Merge into…" control: a native `<select>` of the other entries (by name) and a "Merge" button, disabled until a target is chosen.
- [x] Both paths ask before merging: an inline confirmation reads "Merge Richard Bachman into Stephen King? Richard Bachman and its aliases become aliases of Stephen King." with "Confirm merge" and "Cancel". Nothing is sent until "Confirm merge".
- [x] Confirming calls `POST /api/{kind}/{keepId}/merge` with `{ fromId }`, then reloads the list and the suggestions, and announces "Merged Richard Bachman into Stephen King." in the page's status region. Focus moves to the kept entry's Edit button. Cancel closes the confirmation and returns focus to the button that opened it.
- [x] A 404 (an entry was already merged or removed) shows "That entry no longer exists." as an alert and reloads; a 400 or thrown error shows an alert and leaves the list as it was.
- [x] All four checks pass, and phase 6's done-when lines are met.

## Read first
- `docs/slices/42-names-page.md` (`## Outcome`), and slices 40–41 for the merge and suggestions endpoints
- `src/AudiobookOrganizer.Client/src/pages/NamesPage.tsx` (`load`, `save`, `status`, `editButtons`, `focusId`) and `NamesPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getEntries`, `toSaveEntryResult`) and `types/names.ts`

## Tasks
- [x] **Types + API.** In `types/names.ts` add `MergeSuggestion { keep: CanonicalEntry; merge: CanonicalEntry; reason: string }`. In `services/api.ts` add `getSuggestions(kind)` and `mergeEntry(kind, keepId, fromId)` (returns `SaveEntryResult` through `toSaveEntryResult` with success 200).
- [x] **Confirmation component.** `components/names/MergeConfirm.tsx`: props `{ from: CanonicalEntry, into: CanonicalEntry, onConfirm, onCancel }`; renders the confirmation sentence, "Confirm merge" and "Cancel"; focuses "Confirm merge" when it appears. Used by both paths.
- [x] **Suggestions section.** `components/names/MergeSuggestions.tsx`: props `{ suggestions, failed, onMerge(from, into) }`; renders the section (heading "Possible duplicates"), the empty text, the alert, and per item the reason and the merge button, showing `MergeConfirm` in place of the clicked item's button.
- [x] **Page.** In `NamesPage.tsx` load suggestions alongside entries (a suggestions failure doesn't fail the page), add the per-row "Merge into…" select + button with `MergeConfirm`, and one `merge(from, into)` handler for both paths that sends, reloads both lists, sets the status and focus, and shows the 404/400/error alerts.
- [x] **Tests.** Add the tests below and run all four checks.
- Data: none (client only; uses the slice 40–41 endpoints).
- Auth: none (single user, no login).
- Accessibility: the duplicates section is a `<section>` labelled by its `h2`; each merge button's visible text names both entries; the row select has a visible label ("Merge Richard Bachman into") or `aria-label` naming the row's entry; the confirmation is plain inline content (no modal), its "Confirm merge" button receives focus, Cancel returns focus to the opener; results go through the existing `role="status"` region and errors through `role="alert"`. All native controls.

## Tests to add
- `src/AudiobookOrganizer.Client/src/pages/NamesPage.test.tsx`: the duplicates section lists a suggestion's reason and "Merge S. King into Stephen King"; with `[]` it shows "No possible duplicates."; when `getSuggestions` rejects, the section shows an alert and the entries still list.
- Same file: clicking a suggestion's merge button shows the confirmation and sends nothing; "Confirm merge" calls `mergeEntry('people', keepId, fromId)`, reloads entries and suggestions, announces "Merged S. King into Stephen King." and focuses Stephen King's Edit button.
- Same file: the row "Merge into…" select lists only the other entries, "Merge" is disabled until one is chosen; Cancel on the confirmation sends nothing and returns focus to "Merge".
- Same file: a 404 result shows "That entry no longer exists." and reloads; a thrown error shows an alert and doesn't reload.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`; on `/people` add `Stephen King` and `S. King`, see the suggestion, merge it, and check `S. King` now appears under "Also known as". Merge two genres through the row control on `/genres`.
- Keyboard only: open a merge, Cancel (focus returns), open again, Confirm (focus on the kept entry's Edit); a screen reader announces the merge.

## Out of scope
- Dismissing a suggestion ("not the same") so it stops appearing
- Undoing a merge
- Linking entries to books or resolving book values (see Notes)

## Notes
- File count: 6 (`types/names.ts`, `services/api.ts`, `components/names/MergeConfirm.tsx`, `components/names/MergeSuggestions.tsx`, `pages/NamesPage.tsx`, `pages/NamesPage.test.tsx`). The two components are tested through the page.
- A native `<select>` is enough for the target; a shadcn combobox would only matter for very long catalogs (no new package or component).
- An inline confirmation rather than a dialog keeps it plain HTML; merges have no undo, so the confirmation is required.
- With this slice, phase 6's done-when lines ("aliases map variants to one canonical entry; the app suggests but never auto-merges aliases; a management screen lets the user create, merge and edit canonical entries") should be met, so the review should end the phase (gate 5).
- For the developer at the end of the phase: nothing yet links a book's `authors`, `narrators` (and later genres) to canonical entries or marks values `normalized`. The PRD wants normalization "before final metadata writing and file organization", so it likely belongs at the start of phase 7 or 8; that's a PLAN.md change for the developer to approve (gate 4). Dismissing suggestions is the other candidate follow-up.
- Conflict check: matches the slice 40 merge semantics (`keep` = URL id, `merge` = `fromId`), the slice 41 suggestion shape, and the "never auto-merge" PRD rule.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 243 passed, 0 failed | Unchanged (no API changes) |
| Frontend tests | 215 passed, 0 failed | Was 206 |
| New tests | 4 of 4 present, passing | `NamesPage.test.tsx` now 18 tests (9 new: duplicates list, empty, suggestions failure, suggestion confirm + merge + status + focus, row select and disabled Merge, Cancel focus, row merge, 404 reload, 400 and thrown alerts) |
| Manual: Stephen King / S. King suggestion merged, alias shows; genre row merge | Not run | API (5070) and client (5173) not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: keyboard Cancel/Confirm focus, screen reader announces merge | Not run | Same; needs a screen reader |

Data checks for the developer (not run): `SELECT p.Id, p.Name, a.Alias FROM Person p LEFT JOIN PersonAlias a ON a.PersonId = p.Id;` after a merge (the merged row is gone, its name is an alias).

## Outcome (2026-10-19, commit df7ae25)

**Status:** Implemented
**Summary:** The People and Genres pages show a "Possible duplicates" section from the suggestions endpoint and a "Merge into…" select on each row. Both paths go through an inline `MergeConfirm` ("Confirm merge" / "Cancel") before calling the merge endpoint, then reload, announce "Merged … into …" and focus the kept entry's Edit button; 404, 400 and thrown errors show alerts.
**Changed:** 7 code files: `components/names/MergeConfirm.tsx`, `components/names/MergeSuggestions.tsx` (new), `pages/NamesPage.tsx`, `pages/NamesPage.test.tsx` (9 new tests), `services/api.ts` (`getSuggestions`, `mergeEntry`), `types/names.ts` (`MergeSuggestion`), `components/layout/AppLayout.test.tsx`. Plus this slice file, `docs/PLAN.md` and `docs/CURRENT-STATE.md`.
**Deviations:**
- `AppLayout.test.tsx` (not listed) needed `getSuggestions` and `mergeEntry` in its api mock because the names pages now call them; nothing weakened: *fine*.
- On a row, the confirmation appears below the still-visible control (so Cancel focuses "Merge"); in the suggestions list it replaces the button. The row control shows only when there are at least two entries: *fine*.
- Merge errors use a page-level `role="alert"` under the status region, and focus returns to the opener on failure: *fine*.
**Follow-ups:**
- Linking book values to canonical entries, and dismissing suggestions: both raised as open questions for the developer (not in phase 6's done-when lines).
- Manual checks for 42–43 not run.
