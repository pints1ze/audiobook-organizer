# Slice 58: Approve a book from the Preview page

**Phase:** 8, Processing
**Goal:** each book on the Preview page with no problems has an "Approve" button that queues it for processing (`POST /api/jobs`) and says so; a refusal shows the API's reason.

## Done when
- [x] `services/api.ts` has `approveBook(groupId)` returning `{ ok: true, job }` on 201, `{ ok: false, message }` on 400 and 409 (the problem's `detail`, else its `title`) and on 404 ("The book is no longer in the library."), and throwing on anything else; `types/jobs.ts` mirrors `Models/Api/ProcessingDtos.cs`
- [x] on `/preview`, every book whose files have no problems shows an "Approve" button under its heading; books with problems show "Fix the problems above to approve this book." instead and no button
- [x] pressing Approve disables the button and shows "Approving…"; on success the button is replaced by "Queued for processing." and focus moves to that message; on a refusal the button stays and a `role="alert"` message shows the reason; a thrown error shows "The book couldn't be approved."
- [x] all four checks pass

## Read first
- `src/AudiobookOrganizer.Client/src/pages/PreviewPage.tsx` and `PreviewPage.test.tsx` (`BookSection`, `fileProblems`, `hasProblems`, the test builders)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`startScan` and `pickMatch` for the result-union pattern)
- `src/AudiobookOrganizer.Api/Controllers/JobsController.cs` (status codes and problem titles) and `Models/Api/ProcessingDtos.cs`
- `docs/slices/54-approve-book-queue.md` (what approval refuses and why)

## Tasks
- [x] **Types.** New `src/types/jobs.ts`: `ProcessingJobFile` (`fileId`, `sourcePath`, `destinationPath`, `status`, `error`), `ProcessingJob` (`id`, `groupId | null`, `title | null`, `status`, `createdAtUtc`, `startedAtUtc`, `finishedAtUtc`, `error`, `files`), and `ApproveBookResult` = `{ ok: true; job: ProcessingJob } | { ok: false; message: string }`. Statuses stay `string` (the API's text values).
- [x] **API call.** `approveBook(groupId: number)` in `services/api.ts`: `POST /api/jobs` with JSON `{ groupId }`, mapped as in Done when.
- [x] **Preview page.** In `BookSection`, below the heading, a small `ApproveBook` component (same file; it's used once) with its own state (`idle | approving | queued | refused(message)`). The button's accessible name is "Approve", described by the book's heading (`aria-describedby={headingId}`) so screen readers hear which book. The "Queued for processing." message is a `<p role="status" tabIndex={-1}>` focused with a ref after success. Books with problems render the "Fix the problems…" note instead. Plain `<button>` styled like the page's other controls; no new UI components.
- [x] **Tests** (below). Run all four checks.
- Data: none.
- Auth: none (single user, no login).
- Accessibility: native `<button>` (keyboard by default), accessible name plus `aria-describedby` to the book heading, disabled while approving, focus moved to the "Queued" status after success (the button disappears), refusals announced with `role="alert"`. Contrast uses the existing `text-muted`/`text-destructive` tokens.

## Tests to add
- `PreviewPage.test.tsx`: a clean book shows Approve and a book with a problem shows the "Fix the problems…" note and no Approve button.
- `PreviewPage.test.tsx`: pressing Approve calls `approveBook(1)`, shows "Approving…" with the button disabled while pending, then "Queued for processing." with focus on it and no button.
- `PreviewPage.test.tsx`: a refusal (`{ ok: false, message: 'A file is already at X.' }`) shows that message in an alert and keeps the button enabled; a rejected promise shows "The book couldn't be approved."
- New `src/services/api.test.ts` cases, or the existing api test file if there is one: `approveBook` maps 201, 400/409 (detail, falling back to title) and 404, and throws on 500 (mock `fetch`).

## Manual checks
- With the API and client running, open `/preview` on a scanned library with a clean book: press Approve with the keyboard; "Queued for processing." is announced and focused; `GET /api/jobs` shows the job, and it ends `Succeeded`.
- Press Approve on the same book again after a reload: the alert says why it can't be approved (already queued, or a file already exists).

## Out of scope
- A Jobs page listing jobs, their files and failure details, and Retry (next slice, with the backend retry-without-duplicates tests)
- An Approve button on the book page
- Hiding or marking books that are already processed on the Preview page (they show "Already exists"; phase 9's repeat-scan work)
- Approving several books at once

## Notes
- File count: 4–5 (new `types/jobs.ts`, `services/api.ts`, `pages/PreviewPage.tsx`, `pages/PreviewPage.test.tsx`, and the api test file if one exists or is added). Under the cap.
- Why next: phase 8's done-when needs failures "visible with details" and retry; both need jobs in the UI, and today a job can only be created with `curl`. Approve is the entry point the Jobs page builds on.
- Retry plan for slice 59 (assumption, reversible): retrying a failed book is approving it again, which re-snapshots destinations from the current preview, so a fix to metadata or templates takes effect. The failed job stays as history. No duplicates follow from the existing rules: a failed job removes its copies, approval refuses existing destinations, and copies never overwrite. Slice 59 proves this with tests and gives failed jobs a Retry button; no new endpoint.
- `getJobs` waits for slice 59, where it's first used (YAGNI).
- Conflict check: consistent with AGENTS.md (state in React built-ins plus `services/api.ts`; plain HTML buttons; no new packages; WCAG 2.1 AA), slice 54's approval rules and the 409/400/404 contract. No backend change.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 367 passed, 0 failed | Unchanged (client-only slice). TRX `slice58-tests.trx` |
| Frontend tests | 260 passed, 0 failed | Was 249 |
| New tests | 4 of 4 present, passing | `PreviewPage.test.tsx`: Approve vs the fix note; Approving… then focused "Queued for processing."; refusal alert keeps the button; thrown error shows the generic alert. `api.test.ts` `approveBook`: 201, 400 detail, 409 title fallback, 404, throws on 500 |
| Manual: keyboard Approve on a clean book, job ends `Succeeded` | Not run | API and client not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: Approve again after a reload shows the reason | Not run | Same |

Data checks: none (no schema change).

## Outcome (2026-10-19, commit 622b0a0)

**Status:** Implemented
**Summary:** `approveBook` in `services/api.ts` (with `types/jobs.ts`) posts to `/api/jobs` and maps 201, 400/409 and 404 to a result union. Each clean book on `/preview` has an Approve button described by its heading; it shows "Approving…", then a focused "Queued for processing." status, or the API's reason in an alert. Books with problems show a fix note instead.
**Changed:** 6 files: new `types/jobs.ts`; `services/api.ts`, `services/api.test.ts`, `pages/PreviewPage.tsx`, `pages/PreviewPage.test.tsx`; the slice file.
**Deviations:**
- A 400/409 with neither detail nor title falls back to "The book can't be approved yet.": *fine*.
- The Approve button reuses the Settings page's primary button style: *fine*.
**Follow-ups:**
- Next (59): a Jobs page listing jobs with their files and failure details, a Retry button on failed jobs (approves the book again), `getJobs`, and backend tests proving retry creates no duplicates.
- Before the custom-fields slice: the developer answers the Audiobookshelf frames question (gate 1 until then).
