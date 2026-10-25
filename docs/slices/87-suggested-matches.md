# Slice 87: Suggested matches

**Phase:** 11, Parallel identification
**Goal:** the Review pane shows a book's stored candidates side by side with their scores, finds them on request, and accepts one with a click.

## Done when
- [x] the pane's Match section has a "Suggested matches" area that loads `GET /api/groups/{id}/candidates`: candidates as cards side by side (cover, title, author, narrator, runtime, score as "Score 87 of 100"), best first
- [x] a book never searched shows "Not searched yet." and a "Find suggestions" button that calls `POST .../candidates` and shows the result; a stored search shows when it ran and a "Search again" button
- [x] stored provider errors and a search with no candidates are shown as text, not as an empty area
- [x] each card's "Accept match" saves it through `PUT .../match` exactly as a Catalog search pick does (same `onPicked` path: status, focus, list reload); the card equal to the current match says "Current match" instead
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docs/UIUX.md` (one filled primary button per pane state; status and alert roles; focus after actions)
- `docs/slices/86-stored-candidates.md` (the API this slice uses)
- `src/AudiobookOrganizer.Client/src/components/review/BookPane.tsx` and `components/book/CatalogSearch.tsx` (`CandidateCover`, `CandidateDetails`, `isSameCandidate`, the pick handling to mirror)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getMatch`, `pickMatch`) and `types/metadata.ts`
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.test.tsx` (how `services/api` is mocked)

## Tasks
- [x] **Types and API.** In `types/metadata.ts` add `ScoredCandidate { candidate: MetadataCandidate; score: number }` and `BookCandidates { groupId; searchedAtUtc: string; candidates: ScoredCandidate[]; errors: string[] }`. In `services/api.ts` add `getCandidates(groupId): Promise<BookCandidates | null>` (204 → null, other non-200 throws) and `findCandidates(groupId): Promise<BookCandidates>` (`POST`, non-200 throws). Verify: typecheck.
- [x] **Component.** New `components/review/SuggestedMatches.tsx` (`groupId`, `currentMatch`, `onPicked`). Loads on mount; shows a skeleton line while loading and an alert when loading fails. Cards in a responsive grid (`grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]`), each an `<li>` in a `<ul>` with its title as text and `CandidateCover`/`CandidateDetails` reused from `CatalogSearch.tsx`. The best acceptable card's button is filled; the rest are outline. Accept uses `pickMatch` and shows a pick error beside the card as `role="alert"`. "Find suggestions"/"Search again" is an outline button that disables while searching and announces "Searching…" then "N suggestions found." in a `role="status"`. Export `isSameCandidate` from `CatalogSearch.tsx` and use it for "Current match".
- [x] **Pane.** In `BookPane.tsx` render `SuggestedMatches` in the Match section between the chosen match and Catalog search, with the same `currentMatch` and `handlePicked`. Add `getCandidates`/`findCandidates` to the `services/api` mock in `pages/ReviewPage.test.tsx` (default: `getCandidates` resolves null) so its tests keep passing unchanged.
- [x] **Tests** (below), then all four checks.
- Data: none.
- Auth: none (no auth in the app).
- Accessibility: the area is a `<section>` with an `h3` "Suggested matches"; each Accept button's accessible name includes the title and score ("Accept match: Project Hail Mary, score 87 of 100"); buttons in DOM order (left to right, top to bottom); after a successful accept focus goes to the Match heading through the existing `handlePicked`; when "Find suggestions" disappears after a search, focus moves to the area's heading.

## Tests to add
- `components/review/SuggestedMatches.test.tsx`: stored candidates render as cards best first with their scores and the search time; a provider error and an empty result show their text.
- Same file: a never-searched book shows "Not searched yet."; "Find suggestions" calls `findCandidates`, shows the cards and moves focus to the heading.
- Same file: "Accept match" calls `pickMatch` with the candidate and then `onPicked`; a card equal to `currentMatch` shows "Current match" and no button; a failed pick shows an alert.
- `pages/ReviewPage.test.tsx`: the selected book's pane calls `getCandidates` with its group id and shows a stored suggestion.

## Manual checks
- Restart the dev API, open `/`, pick a book: "Find suggestions" returns cards from the real Audible API; the right book scores highest for a known title; Accept match saves it and the stage chip updates.
- Keyboard only: Tab reaches each Accept button in order with a visible focus ring; the `Enter` shortcut still accepts the highlighted Catalog search result after a search.

## Out of scope
- Searching automatically after a scan (needs the throttle answer, see Open questions).
- Selecting several books and accepting their suggested matches in one action, and batch approve.
- A stage or list badge for "has suggestions".

## Notes
- Conflict check: matches are never applied without a click (PRD item 12); the PRD names the Match section's pane, no new route. `UIUX.md` allows one filled primary button per pane state; the best suggestion's button is filled, and Catalog search's highlighted result only appears after a search, so both can be filled at once after the user searches; accepted as the same "Accept match" action.
- The `j`/`k`/`a`/`Enter` shortcuts are unchanged; `Enter` stays tied to the Catalog search highlight.
- Files touched: `types/metadata.ts`, `services/api.ts`, `components/review/SuggestedMatches.tsx` (new), `SuggestedMatches.test.tsx` (new), `components/review/BookPane.tsx`, `components/book/CatalogSearch.tsx` (export only), `pages/ReviewPage.test.tsx`: 7.
- Later slices for this phase: background search after a scan, throttled; select books and "Accept suggested match"; select Ready books and approve.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings; `-p:OutDir=<scratchpad>` because the running dev API locks `bin/Debug` |
| Typecheck | Passed | |
| Backend tests | 427 passed, 0 failed | |
| Frontend tests | 359 passed, 0 failed | 21 files |
| New tests | 4 of 4 present, passing | `SuggestedMatches.test.tsx` (9 tests: cards best first with scores and time; error and empty result as text; load failure alert; Find suggestions then heading focus; search failure alert; accept via `pickMatch` then `onPicked`; Current match; pick failure alert); `ReviewPage.test.tsx` pane calls `getCandidates` and shows a suggestion |
| Manual: real Audible suggestions, accept updates the stage | Not run | needs the dev API restarted (`scripts/start.ps1`) |
| Manual: keyboard order and `Enter` shortcut | Not run | same |

## Outcome (2026-10-25, commit 58802a3)

**Status:** Implemented
**Summary:** The Review pane's Match section has a Suggested matches area that loads the stored candidates and shows them as cards side by side (cover, details, "Score N of 100"), best first, with "Find suggestions"/"Search again" and an Accept match on each card that goes through the same pick path as Catalog search.
**Changed:** 7 code files (`components/review/SuggestedMatches.tsx` and its test new, `BookPane.tsx`, `CatalogSearch.tsx` export, `services/api.ts`, `types/metadata.ts`, `pages/ReviewPage.test.tsx`)
**Done when:** all 5 met; manual checks not run.
**Deviations:**
- Status says "Search finished." for an empty result (the area already says "No suggestions found."): fine.
- Stored errors are plain paragraphs, not list items: fine.
- A pick on a deleted book says "This book no longer exists." without Catalog search's "Back to Incoming" link: fine, the list reload removes it.
- `data-primary` on the filled button, for the test: fine.
- Focus moves to the heading only after the first search; after "Search again" it stays on the button: fine, matches the slice (focus moves only when the button disappears).
**Follow-ups:** none new; background search and batch actions remain for this phase.
