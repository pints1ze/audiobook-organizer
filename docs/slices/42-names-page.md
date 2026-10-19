# Slice 42: Names page

**Phase:** 6, Normalization
**Goal:** the user can list, create and edit canonical people and genres, with their aliases, on a People page and a Genres page.

## Done when
- [x] The main nav has "People" (`/people`) and "Genres" (`/genres`), between Incoming and Settings, and marks the current one with `aria-current="page"`.
- [x] Each page has an `h1` ("People" / "Genres") and lists every entry from `GET /api/people` or `GET /api/genres` in the API's order: the name, then its aliases ("Also known as: Richard Bachman, John Swithen"; "Also called: Sci-Fi" for genres). An empty catalog shows "No people yet." / "No genres yet."; a failed load shows an alert with a "Try again" button.
- [x] An "Add a person" / "Add a genre" form (name input, aliases textarea, one alias per line) creates the entry, clears the form, shows "Saved Stephen King." and puts it in the list in name order.
- [x] Each entry has an "Edit" button that swaps the row for the same form prefilled; Save replaces the name and aliases (`PUT`), Cancel restores the row. After save or cancel, focus returns to that row's "Edit" button.
- [x] A 400 shows the API's messages next to the field (`name` or `aliases`, e.g. "Already used by Stephen King."), linked with `aria-describedby` and `aria-invalid`, and the form keeps what was typed. A thrown error shows an alert.
- [x] All four checks pass.

## Read first
- `docs/slices/41-merge-suggestions-api.md` (`## Outcome`), and slices 38–39 for the API shapes and error keys
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`saveSettings` 200/400 pattern) and `types/settings.ts` (result union)
- `src/AudiobookOrganizer.Client/src/pages/SettingsPage.tsx` and `SettingsPage.test.tsx` (form, error, status and `vi.mock` patterns)
- `src/AudiobookOrganizer.Client/src/components/book/MetadataEditor.tsx` (list fields edited one name per line, slice 33)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and `AppLayout.test.tsx`

## Tasks
- [x] **Types + API.** `types/names.ts`: `NameKind = 'people' | 'genres'`, `CanonicalEntry { id, name, aliases }`, `EntryRequest { name, aliases }`, `EntryFieldErrors { name?, aliases? }`, `SaveEntryResult` (`{ ok: true; entry }` | `{ ok: false; errors }` | `{ ok: false; notFound: true }`). In `services/api.ts`: `getEntries(kind)`, `createEntry(kind, request)` (201), `updateEntry(kind, id, request)` (200/400/404), each calling `/api/${kind}`.
- [x] **Form component.** `components/names/EntryForm.tsx`: props `{ kind, initial?, onSave(request) → Promise<SaveEntryResult>, onCancel?, submitLabel }`. Name input and aliases textarea, each with a visible label; aliases are split by line, trimmed, blank lines dropped. Shows field errors from a 400 result; keeps input on failure.
- [x] **Page.** `pages/NamesPage.tsx` with prop `kind`: load state (loading / loaded / failed with retry), the add form, the list with per-row Edit using `EntryForm`, status message (`role="status"`) and focus handling from Done when. Labels come from a small map per kind (heading, add label, empty text, "Also known as" / "Also called").
- [x] **Routes + nav.** In `App.tsx` add `/people` → `<NamesPage key="people" kind="people" />` and `/genres` → `<NamesPage key="genres" kind="genres" />` (the key resets state when switching). In `AppLayout.tsx` add the two nav links. Add a nav test to `AppLayout.test.tsx` and mock `getEntries` there.
- [x] **Tests.** Add the tests below and run all four checks.
- Data: none (client only; uses the slice 38–39 endpoints).
- Auth: none (single user, no login).
- Accessibility: one `h1` per page; the list is a `<ul>` with each entry's name as text; each Edit button names its entry (`aria-label="Edit Stephen King"`); inputs have visible `<label>`s; errors use `role="alert"` and are linked with `aria-describedby` + `aria-invalid`; saves announce through a `role="status"` region; focus returns to the row's Edit button after save or cancel, and to the name input when the edit form opens. All native controls, keyboard-operable.

## Tests to add
- `src/AudiobookOrganizer.Client/src/pages/NamesPage.test.tsx`: with `kind="people"` lists entries with their aliases; an empty list shows "No people yet."; a failed load shows the alert and "Try again" reloads.
- Same file: adding a person sends `createEntry('people', { name, aliases: ['Richard Bachman'] })` (blank lines dropped), shows "Saved Stephen King." and lists it; a 400 shows "Already used by Stephen King." on the name input (`aria-invalid`) and keeps the typed text.
- Same file: Edit prefills the form, Save calls `updateEntry('people', id, …)` and shows the new name; Cancel restores the row and focuses its Edit button.
- Same file: with `kind="genres"` the heading is "Genres", the add button says "Add a genre" and `getEntries('genres')` is called.
- `src/AudiobookOrganizer.Client/src/components/layout/AppLayout.test.tsx`: on `/people` the "People" nav link has `aria-current="page"` and the People heading shows.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`; open `/people`, add `Stephen King` with alias `Richard Bachman`, edit it to add `John Swithen`, then add `KING, STEPHEN` and see the clash error. Repeat briefly on `/genres`.
- Keyboard only: Tab to Edit, open, change, save; focus returns to Edit, and a screen reader announces "Saved …".

## Out of scope
- Merging and suggestions on the page (next slice, using the slice 40–41 endpoints)
- Deleting an entry, dismissing suggestions, search or paging the list
- Linking entries to books

## Notes
- File count: 8 (`types/names.ts`, `services/api.ts`, `components/names/EntryForm.tsx`, `pages/NamesPage.tsx`, `pages/NamesPage.test.tsx`, `App.tsx`, `components/layout/AppLayout.tsx`, `AppLayout.test.tsx`). `EntryForm` is tested through the page; no separate test file.
- One page component for both kinds, because the API and behavior are identical; the per-kind labels are the only difference.
- After create or update, reload the list with `getEntries` rather than splicing locally, so order and dropped duplicate aliases match the server.
- No new package or shadcn component: plain form controls and a list. `docs/UIUX.md` doesn't exist; follow the Settings and Book page styling.
- Remaining phase 6 piece after this: merge + suggestions on these pages (slice 43), which should meet the phase's done-when lines.
- Conflict check: matches the slice 33 decision (list values one per line), the API error keys from slices 38–39, and AGENTS.md's stack (React built-ins + `services/api.ts`).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 243 passed, 0 failed | Unchanged (no API changes) |
| Frontend tests | 206 passed, 0 failed | Was 195 |
| New tests | 5 of 5 present, passing | `NamesPage.test.tsx` (9: list with aliases, empty, failed load + retry, add with blank lines dropped, 400 on name keeps input, edit + save, cancel focuses Edit, genres labels); `AppLayout.test.tsx` (People `aria-current`, nav order) |
| Manual: add/edit Stephen King, clash on `KING, STEPHEN`, then `/genres` | Not run | API (5070) and client (5173) not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: keyboard-only edit, focus back to Edit, screen reader "Saved …" | Not run | Same; needs a screen reader |

Data checks for the developer (not run): `SELECT * FROM Person; SELECT * FROM PersonAlias;` after the manual steps.

## Outcome (2026-10-19, commit 8375c9e)

**Status:** Implemented
**Summary:** The nav has People (`/people`) and Genres (`/genres`), both served by one `NamesPage` that lists entries with their aliases, adds them through an `EntryForm` (aliases one per line) and edits each row in place. Saves announce "Saved …"; 400 errors show on the field; focus returns to the row's Edit button.
**Changed:** 8 code files: `types/names.ts`, `components/names/EntryForm.tsx`, `pages/NamesPage.tsx`, `pages/NamesPage.test.tsx` (new, 9 tests); `services/api.ts` (`getEntries`, `createEntry`, `updateEntry`), `App.tsx`, `components/layout/AppLayout.tsx`, `AppLayout.test.tsx`. Plus this slice file, `docs/PLAN.md` and `docs/CURRENT-STATE.md`.
**Deviations:**
- The add form sits in a region headed "New person" / "New genre", with the submit button "Add a person" / "Add a genre": *fine*.
- The nav list wraps (`flex-wrap`) so five links fit at phone width: *improvement*.
- The existing nav-order test now expects People and Genres; nothing weakened: *fine*.
- A 404 on update, or a 400 without a `name`/`aliases` error, shows the form's general "couldn't be saved" alert: *fine*.
**Follow-ups:**
- Merge and suggestions on these pages (slice 43).
- Manual checks for 42 not run (live API and client, keyboard, screen reader).
