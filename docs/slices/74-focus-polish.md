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
