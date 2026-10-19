# Slice 31: Use this match

**Phase:** 5, Metadata
**Goal:** on the book page the user picks a catalog candidate with "Use this match", and the page shows the book's chosen match, also after a reload.

## Done when
- [x] The book page loads `GET /api/groups/{id}/match` and shows a "Chosen match" section (an `h2`) above the catalog search: the match's cover, title, subtitle and the same detail rows as a candidate, plus "Picked {date}" via `formatDateTime`. With no match (204) it reads "No match chosen yet." If loading the match fails, the section says so and search still works.
- [x] Each search result has a "Use this match" button whose accessible name is unique ("Use this match: {title}, result {n} of {count}"). Clicking it calls `PUT /api/groups/{id}/match` with that candidate; all pick buttons are disabled while it saves.
- [x] After a successful pick the Chosen match section shows the new match, a `role="status"` region announces "Match saved: {title}", focus moves to the Chosen match heading, and the picked result reads "Current match" instead of a button. Picking another result replaces it.
- [x] A 400 shows the first error message for that pick (`role="alert"`) and keeps the old match; a 404 shows "This book no longer exists" with a link to Incoming; a network failure shows a retryable error. None of them clears the results.
- [x] All four checks pass.

## Read first
- `docs/slices/30-pick-match-api.md`, `## Outcome` (endpoints, 200 / 204 / 400 / 404)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (validation messages, error keys) and `Models/Api/MetadataDtos.cs` (`BookMatchDto`)
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`CatalogSearch`, `CandidateItem`) and `BookPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`searchMetadata`, `updateGroup` result unions) and `api.test.ts`
- `src/AudiobookOrganizer.Client/src/types/metadata.ts`, `utils/formatDateTime.ts`

## Tasks
- [x] **Types and API calls.** In `types/metadata.ts` add `BookMatch` (`groupId`, `match: MetadataCandidate`, `pickedAtUtc`) and `PickMatchResult` = `{ ok: true; match } | { ok: false; errors: Record<string, string[]> } | { ok: false; notFound: true }`. In `services/api.ts` add `getMatch(groupId): Promise<BookMatch | null>` (200 → match, 204 → null, anything else throws) and `pickMatch(groupId, candidate): Promise<PickMatchResult>` (PUT JSON; 200 / 400 / 404; anything else throws). Verify: `api.test.ts`.
- [x] **Chosen match section.** In `BookPage.tsx`, load the match after the group, then render "Chosen match" above "Catalog search". Split the detail rows out of `CandidateItem` into a small `CandidateDetails` component used by both, so the two can't drift. Verify: `BookPage.test.tsx`.
- [x] **Pick button.** Pass the group id, the current match and an `onPicked` callback into `CatalogSearch`; each `CandidateItem` gets the button or "Current match". A result is the current match when provider, ASIN, ISBN and title all equal the chosen match's. Handle the outcomes in Done when; keep the error beside the result that failed. Verify: `BookPage.test.tsx`.
- Data: none (slice 30 owns the tables).
- Auth: none (single user, no login).
- Accessibility: buttons are real `<button>`s with unique names; saving disables them with `disabled` (not just styling); success is announced by a status region and focus moves to the Chosen match `h2` (`tabIndex={-1}`); errors use `role="alert"` and sit next to the result; the chosen cover is decorative (`alt=""`).

## Tests to add
- `services/api.test.ts`: `getMatch` returns the match on 200 and null on 204; `pickMatch` sends a PUT with the candidate as JSON and returns the match on 200, field errors on 400 and `notFound` on 404.
- `pages/BookPage.test.tsx`:
  - a book with a stored match shows it under "Chosen match" with its picked date; one without shows "No match chosen yet.";
  - searching, then "Use this match" on the second result, saves it, announces "Match saved", moves focus to the Chosen match heading and shows "Current match" on that result;
  - a 400 from the pick shows its message and keeps the previous match; a 404 shows "This book no longer exists" with a link to Incoming;
  - a failing `getMatch` still lets the user search.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, open a book from Incoming, search, click "Use this match", reload the page and confirm the chosen match is still shown. Back on Incoming, the book now shows "Edited".
- Run a rescan from the dashboard and confirm the book page still shows its chosen match.
- Keyboard only: Tab to a result's "Use this match", press Enter, and confirm focus lands on "Chosen match" and the save is announced.

## Out of scope
- Copying the match into the book's canonical fields, value origins, the editor (next slice)
- Clearing a match
- Covers beyond showing the match's cover URL
- The "Corrected by the user" evidence line for a picked match (slice 30 debt)

## Notes
- File count: 5 (`types/metadata.ts`, `api.ts`, `api.test.ts`, `BookPage.tsx`, `BookPage.test.tsx`), all client.
- The pick is explicit and per book: nothing is applied automatically (PRD rule; decision 2026-10-18).
- `getMatch` throws on 404 like `getGroup`; the page has already loaded the group by then, so a 404 there is treated like any other load failure (the slice 29 not-found shortcut stays as is).
- Conflict check: no new package, no new shadcn component, `services/api.ts` stays the only backend caller.
- Remaining phase 5: the editor with value origins that keeps user edits across re-searches; manual completion with no match; covers (embedded, download, upload).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 161 passed, 0 failed | Unchanged (no backend change) |
| Frontend tests | 157 passed, 0 failed | 10 files; was 142 |
| New tests | 7 of 7 present, passing | `api.test.ts` `getMatch` (3) and `pickMatch` (4); `BookPage.test.tsx` 8 (stored match, none, pick + focus + current, replace, 400, 404, network failure, failing `getMatch`) |
| Manual: pick, reload, Incoming shows "Edited" | Not run | API (5070) and client (5173) not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: rescan keeps the chosen match | Not run | Same |
| Manual: keyboard pick, focus and announcement | Not run | Same |

## Outcome (2026-10-19, commit 895138e)

**Status:** Implemented
**Summary:** The book page shows a "Chosen match" section loaded from `GET /api/groups/{id}/match`, and every search result has a uniquely named "Use this match" button that saves it through `PUT`. Success is announced, moves focus to the section and marks the result "Current match"; 400, 404 and network failures show beside the result without clearing anything.
**Changed:** 5 files, all client: `pages/BookPage.tsx` (shared `CandidateDetails` and `CandidateCover`), `pages/BookPage.test.tsx`, `services/api.ts`, `services/api.test.ts`, `types/metadata.ts`.
**Deviations:**
- The 404 message sits beside the failed result with a "Back to Incoming" link instead of replacing the page, so the results stay: *fine*.
- A 400 with no field messages falls back to "This match couldn't be saved.": *fine*.
- A new search clears old pick errors, since result positions change: *fine*.
**Follow-ups:**
- Next slice: the editor (current vs proposed values with their origin, user edits kept across re-searches), fed by the chosen match's stored candidate.
- Still open from earlier slices: the evidence line for a picked match (30) and `getGroup` 404 vs other failures (29).
