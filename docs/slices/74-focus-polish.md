# Slice 74: Focus polish

**Phase:** 9, Hardening & release
**Goal:** keyboard and screen-reader focus lands on the message every time: the Jobs status line on a repeated announcement, and the Incoming load error when a reload after a failed save also fails.

## Done when
- [x] On `/jobs`, retrying two books with the same title in a row moves focus to the status line both times (today the second retry leaves focus where it was, because the text didn't change).
- [x] On `/incoming`, when the page was loaded and a later reload fails (the API went away mid-session), focus moves to the "The incoming groups couldn't be loaded." alert instead of falling back to the body. A failure on the first load doesn't move focus.
- [x] All four checks pass: build, backend tests, typecheck, frontend tests.

## Read first
- `AGENTS.md`, `docs/CURRENT-STATE.md`
- `src/AudiobookOrganizer.Client/src/pages/JobsPage.tsx` (the `announcement` state and the effect that focuses `statusRef`)
- `src/AudiobookOrganizer.Client/src/pages/JobsPage.test.tsx` (the existing Retry tests near "is queued again")
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` (`IncomingPage`: `load`, the `error` state; `IncomingContent` calls `onReload`)
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.test.tsx`
- Follow-ups in `docs/slices/59-jobs-page-and-retry.md` (Outcome) and `docs/slices/26-incoming-polish.md` (Outcome)

## Tasks
- [x] Jobs: make each announcement a new value so the focus effect runs on a repeat, e.g. store `{ text, seq }` (or a separate counter) and depend the effect on it; render `text`. Files: `pages/JobsPage.tsx`. Verify with the new test.
- [x] Incoming: give the load-error `<p role="alert">` a ref and `tabIndex={-1}`; remember (a ref) that the page has been loaded once, and when the state becomes `error` after that, focus the alert. Files: `pages/IncomingPage.tsx`. Verify with the new test.
- [x] Tests for both (below). Files: `pages/JobsPage.test.tsx`, `pages/IncomingPage.test.tsx`.
- Data: none
- Auth: none
- Accessibility: the Jobs status line keeps `role="status"` and `tabIndex={-1}`; the Incoming alert keeps `role="alert"` and gains `tabIndex={-1}` so it can take programmatic focus without entering the tab order. No visual change.

## Tests to add
- `pages/JobsPage.test.tsx`: retry a failed job whose title is "The Hobbit", move focus elsewhere (e.g. to another Retry button), retry a second failed job also titled "The Hobbit"; the status line has focus again. Run it before the fix and see it fail.
- `pages/IncomingPage.test.tsx`: groups load, a save fails (mocked save rejects) and the reload `getGroups` also rejects; the "couldn't be loaded" alert has focus. Run it before the fix and see it fail.
- `pages/IncomingPage.test.tsx`: the first `getGroups` rejects; the alert shows and does not have focus.

## Manual checks
- With the app running (`scripts/start.ps1`), on `/incoming` start editing a group, stop the API, save: the load error is announced and focused (keyboard focus ring on the message, next Tab goes on from there).

## Out of scope
- The cover picker's "Current cover" after a match change (slice 75).
- Any change to what the messages say or to polling.

## Notes
- Conflict check: frontend only, no new packages, no API change; matches AGENTS.md and the decisions in CURRENT-STATE.md.
- Split: the third carried-in item, the cover picker marking a provider cover "Current cover" after the match changes (from slice 37), needs a backend DTO change and its own tests; it goes in slice 75. Plan for it: `ProviderCoverDto` gains `IsChosen`, true only when the chosen cover's source is `provider` and it was chosen at or after the current match's `PickedAtUtc` (`Services/CoverService.cs`, `Models/Api/MetadataDtos.cs`, `GroupCoverControllerTests.cs`, `types/metadata.ts`, `components/book/CoverPicker.tsx` and its test). No schema change.
- The accepted intermittent `JobsPage` Retry failure (slice 62) is near these tests; a new failure in another test is still a gate.

## Test results (2026-10-24)

**Regressions:** one backend test failed once and couldn't be reproduced: the first full run gave 399 passed, 1 failed. Its name wasn't captured because only the summary line was kept. The next 9 runs all passed (400 of 400). This slice changes only frontend files, so the failure isn't caused by it. It isn't on CURRENT-STATE.md's accepted list (gate 2).

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 399 passed, 1 failed (run 1); 400 passed (runs 2–10) | intermittent, test not identified |
| Frontend tests | 274 passed, 0 failed | 16 files |
| New tests | 3 of 3 present, passing | Jobs repeat-announcement focus; Incoming reload-failure focus; Incoming first-load failure doesn't take focus |
| Manual: stop the API mid-edit on `/incoming`, save, load error focused | Not run | app not running; start with `scripts/start.ps1` |

## Outcome (2026-10-24, commit 9006e6d)

**Status:** Implemented
**Summary:** The Jobs status line takes focus on every announcement, including a repeat of the same text, because each one is stored as a new object. The Incoming load error takes focus (`tabIndex={-1}`) when a reload fails after the page has loaded once, but not when the first load fails.
**Changed:** 6 files: `pages/JobsPage.tsx`, `pages/IncomingPage.tsx` and their two test files, plus this slice file and `docs/PLAN.md` (the writer's edits, in the feat commit as in slice 73).
**Deviations:**
- The Jobs announcement is stored as `{ text }`, not `{ text, seq }`; a new object already re-runs the effect: *fine*.
- An unidentified backend test failed once in 10 runs (frontend-only slice): *debt*; the developer accepted it on 2026-10-24.
**Follow-ups:**
- Next: slice 75, the cover picker marking a provider cover "Current cover" only when it came from the current match (plan in this slice's Notes).
- Manual (developer): stop the API mid-edit on `/incoming`, save, and confirm the load error is focused.
- Debt: name and fix the intermittent backend test if it shows up again (keep the full `dotnet test` output, or use a trx logger).
