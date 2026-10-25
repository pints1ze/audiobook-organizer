# Slice 75: Current provider cover

**Phase:** 9, Hardening & release
**Goal:** the cover picker shows "Current cover" on the match's cover only when the stored cover was downloaded from the current match, not from an earlier one.

## Done when
- [x] `GET /api/groups/{id}/cover` returns `provider.isChosen`: `true` only when the chosen cover's source is `provider` and it was chosen at or after the current match's `PickedAtUtc`; `false` otherwise (no chosen cover, an embedded or uploaded one, or a provider cover chosen before the match was picked again).
- [x] The `PUT .../cover` responses carry the same flag (they return the same `BookCoverDto`).
- [x] On the book page's Cover section, after picking a new match, the match's option shows its "Use the match's cover" button again, even though the chosen cover still came from the old match; the chosen-cover image and its "From the match (…)" text stay as they are.
- [x] All four checks pass: build, backend tests, typecheck, frontend tests.

## Read first
- `AGENTS.md`, `docs/CURRENT-STATE.md`
- `src/AudiobookOrganizer.Api/Services/CoverService.cs` (`GetCovers`, the private `ProviderCover`)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs` (`ProviderCoverDto`, `ChosenCoverDto`)
- `src/AudiobookOrganizer.Api/Models/Local/Audiobook.cs` (`MetadataMatch.PickedAtUtc`, `AudiobookCover.ChosenAtUtc`; both round-trip `"O"` strings)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (how a pick stores `PickedAtUtc`; `ToDto` parses it)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs` (the provider tests from slice 35)
- `src/AudiobookOrganizer.Client/src/types/metadata.ts` (`ProviderCover`), `components/book/CoverPicker.tsx` and its test
- Slice 37's Outcome (`docs/slices/37-cover-picker.md`) for the original debt

## Tasks
- [x] Backend: add `bool IsChosen` to `ProviderCoverDto`. In `CoverService`, compute it from the current match and the stored cover: source `provider` and `DateTimeOffset.Parse(cover.ChosenAtUtc) >= DateTimeOffset.Parse(match.PickedAtUtc)` (invariant culture, round-trip kind, as `AudiobookService.ToDto` does). Read the cover once in `GetCovers` and pass it in; `ChooseCoverAsync` still only needs the URL. Files: `Models/Api/MetadataDtos.cs`, `Services/CoverService.cs`.
- [x] Backend tests (below). File: `GroupCoverControllerTests.cs`.
- [x] Client: add `isChosen: boolean` to `ProviderCover`; `CoverPicker` shows "Current cover" for the match's option when `provider.isChosen`, not when `chosen?.source === 'provider'`. Update the `ProviderCover` fixtures the typecheck flags (`CoverPicker.test.tsx`, `pages/BookPage.test.tsx`, `services/api.test.ts` if typed). Files: `types/metadata.ts`, `components/book/CoverPicker.tsx` and those tests.
- Data: none. No schema change; both timestamps are already stored.
- Auth: none
- Accessibility: unchanged markup. When the button shows again it keeps its `aria-label` ("Use the match's cover: Audible"); after a choice focus still moves to the section heading as in slice 37.

## Tests to add
- `GroupCoverControllerTests.cs`: choose the provider cover, then pick a different match (`PUT .../match` with another candidate that has a cover URL); `GET .../cover` has `chosen.source == "provider"` and `provider.isChosen == false`. Run it before the fix (as an assertion on the new field it fails to compile or reads missing, so start with the DTO change and see the value wrong) and see it fail.
- `GroupCoverControllerTests.cs`: right after `PUT .../cover` with `source: "provider"`, the response and `GET .../cover` have `provider.isChosen == true`; with an embedded cover chosen, `provider.isChosen == false`.
- `CoverPicker.test.tsx`: with `chosen.source: 'provider'` and `provider.isChosen: false`, the match's option shows the "Use the match's cover: Audible" button and no "Current cover"; with `isChosen: true` it shows "Current cover".

## Manual checks
- With the app running, on a book's page pick a match with a cover, choose "Use the match's cover", then pick a different match with a cover: the match's option offers its button again; choosing it replaces the chosen image.

## Out of scope
- Storing the cover's source URL or a schema change.
- Re-picking the same match again: it gets a new `PickedAtUtc`, so its cover offers the button again although the image is the same; re-choosing just downloads it again. Acceptable; noted here.
- Any change to `sourceText` ("From the match (…)").

## Notes
- Conflict check: matches CURRENT-STATE.md's cover decisions (slices 34, 35: covers download only from the stored match's URL; one chosen cover per book). No new packages; API change is additive.
- Files: 2 backend, 1 backend test, 1 type file, `CoverPicker.tsx`, up to 3 client test files = about 8, at the cap.
- This is the last carried-in phase 9 item; after its review the phase's done-when lines are met and `/itc-next` opens the phase PR.

## Test results (2026-10-24)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 401 passed, 0 failed | trx logger; the accepted intermittent failure didn't occur |
| Frontend tests | 275 passed, 0 failed | 16 files |
| New tests | 2 of 2 present, passing | `GroupCoverControllerTests.Provider_is_chosen_only_while_the_chosen_cover_came_from_the_current_match` (covers the re-pick, PUT response and embedded cases); `CoverPicker.test.tsx` button vs "Current cover" by `isChosen` |
| Manual: pick a match, use its cover, pick another match, button offered again | Not run | app not running; start with `scripts/start.ps1` |

## Outcome (2026-10-24, commit 4eeac97)

**Status:** Implemented
**Summary:** `ProviderCoverDto` gains `IsChosen`, true only when the chosen cover's source is `provider` and it was chosen at or after the current match's `PickedAtUtc`. The cover picker marks the match's option "Current cover" from that flag, so after a new match it offers "Use the match's cover" again.
**Changed:** 10 files: `Services/CoverService.cs`, `Models/Api/MetadataDtos.cs`, `GroupCoverControllerTests.cs`, `types/metadata.ts`, `components/book/CoverPicker.tsx` and its test, the `BookPage.test.tsx` and `api.test.ts` fixtures, this slice file and `docs/PLAN.md`.
**Deviations:**
- A private `ParseUtc` helper in `CoverService`, also used by its `ToDto`: *fine*.
- Done-when item 3 is ticked from the backend re-pick test plus the CoverPicker test; the browser check is the developer's: *fine*.
**Follow-ups:**
- Manual (developer): the cover re-pick check above, at phase verification.
- Phase 9's done-when lines are met; open the phase PR.
