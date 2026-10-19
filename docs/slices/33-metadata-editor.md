# Slice 33: Metadata editor

**Phase:** 5, Metadata
**Goal:** the book page shows each stored metadata value with its origin, next to the chosen match's value when they differ, and lets the user edit and save the values.

## Done when
- [x] The book page has a "Metadata" section (`h2`) between "Chosen match" and "Catalog search". It loads `GET /api/groups/{id}/metadata` and lists all 12 fields with a label, the current value ("Not set" when empty) and an origin label: "From files", "From Audible", "From Open Library" (other providers by their id), "Edited by you", or nothing when origin is null. Authors and narrators show as a comma-separated list. Series shows the series name; series part is its own row.
- [x] When a match is chosen and a field's current value differs from the match's value for it (and the match has one), the row also shows "Match: <value>". So a user value kept over a pick is visible beside the provider's proposal.
- [x] An "Edit metadata" button switches the section to a form: one labelled text input per text field, and one labelled textarea per list field (authors, narrators) with one name per line. "Save" sends `PUT /api/groups/{id}/metadata` with only the fields the user changed: text trimmed, `""` for a cleared text field, lists split by line with blank lines dropped (`[]` when cleared). With nothing changed, Save closes the form without a request. "Cancel" closes it and discards the edits.
- [x] A 400 shows each message under its field's input (`aria-invalid`, `aria-describedby`), keeps the form open and focuses the first invalid input. A 404 shows "This book no longer exists." with a link to Incoming. A network failure shows a retryable error. On success the form closes, the section shows the returned values, a status line reads "Metadata saved." and focus moves to the "Metadata" heading.
- [x] After "Use this match" succeeds, the section reloads the metadata so the copied provider values show.
- [x] All four checks pass.

## Read first
- `docs/slices/32-metadata-values-api.md`, `## Outcome` (PUT body shape; `null` means "not sent"; `""`/`[]` clears)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs` (`BookMetadataDto`, `BookMetadataFieldsDto`, `UpdateBookMetadataFieldsDto`)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`UpdateMetadata`, `CleanText`, `CleanPeople`, `Fields`: the 400 error keys are the camelCase field names)
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`BookContent`, `handlePicked`, the chosen-match heading focus, `inputClass`, error styling) and `BookPage.test.tsx` (fixtures, the `vi.mock` of `services/api`)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getMatch`, `pickMatch`) and `services/api.test.ts`
- `src/AudiobookOrganizer.Client/src/types/metadata.ts`

## Tasks
- [x] **Types and API.** In `types/metadata.ts` add `MetadataField<T> { value: T; origin: 'file' | 'provider' | 'user' | null; provider: string | null }`, `BookMetadataFields` (the 12 fields; `authors`/`narrators` as `MetadataField<string[]>`, the rest `MetadataField<string | null>`), `BookMetadata { groupId; fields }`, `UpdateBookMetadataFields` (every field optional) and `SaveMetadataResult` (same shape as `PickMatchResult`, with `metadata`). In `services/api.ts` add `getMetadata(groupId)` (throws on non-200) and `saveMetadata(groupId, fields)` (PUT `{ fields }`; 200 → ok, 400 → errors, 404 → notFound, else throw). Verify: `api.test.ts`.
- [x] **Read-only view.** New `components/book/MetadataEditor.tsx` taking `groupId`, `match: MetadataCandidate | null` and a `reloadKey` number. It loads the metadata (loading and failed states like the chosen match's) and renders the rows as a `dl`, with the origin label and the "Match:" line. Map candidate → field values in one small function (`series.name` → `series`, `series.part` → `seriesPart`). Verify: `MetadataEditor.test.tsx`.
- [x] **Edit form.** In the same component: the Edit / Save / Cancel flow, the changed-fields-only request, field errors, 404 and network errors, the saved status and heading focus, per Done when. Disable Save while saving. Verify: `MetadataEditor.test.tsx`.
- [x] **Wire into the book page.** In `BookPage.tsx` render `<MetadataEditor>` between the two sections, passing the current match and a counter that `handlePicked` increments. In `BookPage.test.tsx` add `getMetadata`/`saveMetadata` to the mock with a default resolved value in `beforeEach`, so the existing tests keep passing, and add the reload test. Verify: `npm test`.
- Data: none (the API from slice 32 is unchanged).
- Auth: none (single user, no login).
- Accessibility: every input and textarea has a visible `<label>`; the textareas' hint ("One name per line") is tied in with `aria-describedby`. Errors use `aria-invalid` and `aria-describedby` with an icon plus text, not colour alone. Focus goes to the first invalid input on a 400 and to the "Metadata" heading (`tabIndex={-1}`) on save. Cancel returns focus to "Edit metadata". Status text uses `role="status"`, errors `role="alert"`. Origin labels are text, not colour.

## Tests to add
- `services/api.test.ts`: `saveMetadata` sends `PUT /api/groups/4/metadata` with `{ fields }` and maps 200, 400 and 404; `getMetadata` returns the parsed body.
- `components/book/MetadataEditor.test.tsx`: shows each field with its origin label ("From files", "From Audible", "Edited by you") and "Not set" for empty fields; shows "Match: Ray Porter" for a user narrator that differs from the match's.
- `components/book/MetadataEditor.test.tsx`: editing only the narrators (two lines and a blank line) and clearing the publisher sends exactly `{ narrators: ['A', 'B'], publisher: '' }`; then shows "Metadata saved." with the returned values and focus on the heading. Save with no change makes no request.
- `components/book/MetadataEditor.test.tsx`: a 400 on `title` shows "The title is required." under the Title input, marks it invalid and focuses it; the form stays open.
- `pages/BookPage.test.tsx`: after "Use this match" succeeds, `getMetadata` is called again and the section shows the new values.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, open a book from Incoming, and check the Metadata section shows the file title and author as "From files".
- Pick a match: the values switch to "From Audible". Edit the narrator and save: it reads "Edited by you". Pick a different result: the narrator stays, with a "Match:" line showing the provider's narrator.
- Keyboard only: Tab to "Edit metadata", edit, Save and Cancel; check focus lands where Done when says and the labels read well in a screen reader.

## Out of scope
- Completing a book with no match beyond what this form already allows (the "manual completion" polish: required-field hints, a "mark complete" state) is the next slice
- Covers: embedded, download, upload (a later phase 5 slice)
- Reverting a user value to the provider's value with one click (could follow if the "Match:" line proves useful)
- Showing values from several search results side by side; "proposed" here is the chosen match only
- Syncing an edited title or author back to the group shown on Incoming

## Notes
- File count: 7 (`types/metadata.ts`, `services/api.ts`, `services/api.test.ts`, new `components/book/MetadataEditor.tsx`, new `components/book/MetadataEditor.test.tsx`, `pages/BookPage.tsx`, `pages/BookPage.test.tsx`).
- The editor goes in its own component because `BookPage.tsx` is already ~390 lines; it follows `components/incoming/` (feature folder, test beside it).
- "Current vs proposed" is read as: current = the stored value with its origin, proposed = the chosen match's value, shown only where they differ. A pick still applies values by slice 32's rules; nothing here applies a proposal by itself.
- Lists use one name per line so names with commas ("Smith, Jr.") stay whole.
- Conflict check: no new package or shadcn component (plain inputs and textareas). The `"file"` label for a group title the user renamed on Incoming stays the slice 32 shortcut. Limits and messages come from the API; the client doesn't duplicate them.
- Split off: manual completion polish and covers (later phase 5 slices).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 169 passed, 0 failed | Unchanged (no API change) |
| Frontend tests | 175 passed, 0 failed | Was 157; 11 files |
| New tests | 5 of 5 present, passing | `api.test.ts` (6: getMetadata 200/other, saveMetadata PUT body + 200/400/404/other); `MetadataEditor.test.tsx` (10: origin labels and "Not set", "Match: Ray Porter", changed-fields-only save with status and focus, no-change save, 400 on title, plus load failure, reload key, Cancel, 404, network error); `BookPage.test.tsx` (2: section order, reload after "Use this match") |
| Manual: file values show "From files" | Not run | API (5070) and client (5173) not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: pick → "From Audible", edit narrator, re-pick keeps it with "Match:" line | Not run | Same |
| Manual: keyboard and screen reader pass | Not run | Same |

Data checks for the developer (not run): `SELECT Field, ValueJson, Origin, Provider FROM AudiobookField f JOIN Audiobook b ON b.Id = f.AudiobookId WHERE b.GroupId = <id>;` after saving an edit: the edited fields have `Origin = 'user'`.

## Outcome (2026-10-19, commit fc4e914)

**Status:** Implemented
**Summary:** The book page now has a "Metadata" section, in the new `components/book/MetadataEditor.tsx`, that lists all 12 stored values with their origin ("From files", "From Audible", "Edited by you") and a "Match: …" line where the chosen match proposes something different. An Edit form saves only the changed fields through the new `saveMetadata`, with field errors, 404 and network errors, and focus handling. The section reloads after "Use this match".
**Changed:** 7 files: new `components/book/MetadataEditor.tsx` and `MetadataEditor.test.tsx`, `pages/BookPage.tsx` (renders the editor, reload counter), `pages/BookPage.test.tsx`, `services/api.ts` (`getMetadata`, `saveMetadata`), `services/api.test.ts`, `types/metadata.ts`.
**Deviations:**
- `providerNames` moved from `BookPage.tsx` into `MetadataEditor.tsx` and is imported back, so both sections name providers the same way: *fine*.
- The BookPage heading-order assertion now expects `['Chosen match', 'Metadata', 'Catalog search']`. The new section requires it; the test isn't weakened: *fine*.
- Each shown value sits in its own `<span>` so the existing `getByText(..., { selector: 'dd' })` test still finds one element: *fine*.
- Changes are compared after cleaning (a space-padded title isn't sent), a no-change Save returns focus to "Edit metadata", and a 400 with keys matching no field shows a form-level alert: *improvement*.
- The implementer left the slice file and the PLAN.md edit for the docs commit instead of `git add -A`, following slice 32: *fine*.
**Follow-ups:**
- Next phase 5 slice: manual completion with no match (required-field hints, a way to mark the metadata complete), then covers (embedded, download, upload).
- A one-click "use the match's value" for a field showing a "Match:" line, if it proves useful.
- Still open: the evidence line for a picked match (30), `getGroup` 404 vs other failures (29), the group-title "file" label shortcut (32).
