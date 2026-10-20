# Slice 59: Jobs page with failure details and Retry

**Phase:** 8, Processing
**Goal:** a Jobs page (`/jobs`) lists every processing job with its status, times, error and each file's destination, status and error, refreshes while jobs are queued or running, and offers Retry on a book's failed latest job; tests prove a retry creates no duplicates.

## Done when
- [x] `getJobs()` in `services/api.ts` returns `GET /api/jobs` (newest first) and throws on a non-200
- [x] `/jobs` is in the main nav ("Jobs", after Preview) and shows one section per job: the title (linked to `/books/:groupId` when the group still exists, "Unknown" when the title is empty), status, created and finished times (`formatDateTime`), the job's error, and each file's destination file name and full path, status and error; with no jobs it says "No jobs yet." and links to `/preview`
- [x] while any job is `Queued` or `Running`, the list reloads every 2 seconds, and stops when none is; a failed load shows an alert
- [x] a `Failed` job that is its book's newest job (and whose group still exists) has a "Retry" button that calls `approveBook(groupId)`; on success the list reloads and a focused `role="status"` line says "<title> is queued again."; a refusal shows its reason in an alert beside the button
- [x] backend tests show a retried book ends with exactly one copy of each file, no `.partial` left and the failed job kept as history; and that approving a book whose job succeeded is refused, so it can't be copied twice
- [x] all four checks pass

## Read first
- `docs/slices/58-approve-from-preview.md` and its Outcome (the retry decision in its Notes)
- `src/AudiobookOrganizer.Client/src/pages/PreviewPage.tsx` (`ApproveBook`: states, alert, focus) and `pages/DashboardPage.tsx` (the `setTimeout` polling pattern and its cleanup)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and `AppLayout.test.tsx`
- `src/AudiobookOrganizer.Client/src/types/jobs.ts`, `utils/formatDateTime.ts`
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs` (the existing-destination and failed-job tests to build the retry cases from)

## Tasks
- [x] **API call.** `getJobs(): Promise<ProcessingJob[]>` in `services/api.ts`, with an `api.test.ts` case (200 and a throw on 500).
- [x] **Jobs page.** New `pages/JobsPage.tsx`: load with `getJobs`; states `loading | loaded | error`. One `<section aria-labelledby>` per job with an `<h2>` title, a `<dl>` for status and times, the job error in `text-destructive` with the `CircleAlert` icon (as on Preview), and a `<ul>` of files. Poll with `setTimeout` (2 s) only while some job is `Queued` or `Running`; clear the timer on unmount and on each reload. Retry: shown on the newest job per `groupId` when it is `Failed` and `groupId` isn't null; a small `RetryJob` component in the same file with `idle | retrying | refused(message)`; the button is described by the job heading (`aria-describedby`) and disabled while retrying. On success the page reloads the list (which restarts polling) and focuses a page-level `<p role="status" tabIndex={-1}>`.
- [x] **Route and nav.** `App.tsx`: `<Route path="/jobs" element={<JobsPage />} />`. `AppLayout.tsx`: a "Jobs" `NavLink` after Preview; update `AppLayout.test.tsx` if it lists the links.
- [x] **Backend retry tests** in `JobsControllerTests` (no production change expected; if one is needed, it's a deviation to report).
- [x] **Tests** (below). Run all four checks with full backend output (TRX).
- Data: none.
- Auth: none (single user, no login).
- Accessibility: one `h1` ("Jobs"), an `h2` per job, sections labelled by their headings, the nav link with `aria-current` from `NavLink`, Retry as a native button described by its job heading and disabled while working, refusals and load failures in `role="alert"`, the "queued again" line in a focused `role="status"` (the Retry button disappears after success), status shown as text not colour alone. Polling must not move focus or re-announce unchanged content.

## Tests to add
- New `pages/JobsPage.test.tsx`: a succeeded and a failed job render their titles (linked when `groupId` is set), statuses, the job error and each file's destination, status and error; with no jobs the empty state links to `/preview`.
- `JobsPage.test.tsx`: with a `Running` job the list reloads after 2 s (fake timers) and stops polling once every job is finished.
- `JobsPage.test.tsx`: Retry shows only on a book's newest failed job; pressing it calls `approveBook` with the group id, reloads, and focuses "<title> is queued again."; a refusal shows its message in an alert.
- `JobsControllerTests`: a job that fails because a destination appeared (the existing-destination setup), retried by approving the book again after the blocking file is removed, ends `Succeeded`; the library holds exactly one copy of each file and no `.partial`; the first job is still `Failed` with its error.
- `JobsControllerTests`: after a job succeeds with keep-source on, approving the same book again is a 400 naming an existing file, and no second job is recorded.

## Manual checks
- With the API and client running, approve a book on `/preview`, open `/jobs` from the nav with the keyboard: the job goes from Queued/Running to Succeeded without a reload, and focus stays put.
- Make a job fail (e.g. put a file at one destination after approving), check its error and file errors show on `/jobs`, remove the file, press Retry: "… is queued again." is announced and the new job succeeds; the library has one copy of each file.

## Out of scope
- An Approve button on the book page
- Cancelling or deleting jobs, clearing job history
- Dashboard job counts
- The Audiobookshelf custom fields (wait on the open tag-frame question)
- Retry of a `Succeeded` job whose sources couldn't be deleted (the copies exist; nothing to redo)

## Notes
- File count: 8 (`services/api.ts`, `services/api.test.ts`, new `pages/JobsPage.tsx`, new `pages/JobsPage.test.tsx`, `App.tsx`, `components/layout/AppLayout.tsx`, `AppLayout.test.tsx` if it lists links, `Tests/Controllers/JobsControllerTests.cs`). At the cap.
- Why next: phase 8 needs failures "visible with details" and "retry creates no duplicates"; this slice gives both. After it, only the Audiobookshelf custom fields remain, and they need the developer's answer on tag frames.
- Retry is approving again (slice 58 decision): destinations are re-snapshotted from the current preview, so fixes to metadata or templates take effect; the failed job stays as history. "Newest job per book" uses the list order (the API returns newest first).
- Retry button text is "Retry" so it reads like an action on that job; its description is the job's heading.
- Conflict check: consistent with AGENTS.md (one page per route with its test beside it; state in React built-ins plus `services/api.ts`; plain HTML; no new packages; WCAG 2.1 AA), slice 55's cleanup rules (a failed job removes its copies) and slice 54's approval refusals. No backend production change.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 369 passed, 0 failed | Was 367. TRX `slice59-tests.trx` |
| Frontend tests | 271 passed, 0 failed | Was 260 |
| New tests | 5 of 5 present, passing | `JobsPage.test.tsx` (8): job details, links and empty state; polling every 2 s that stops when nothing is active; Retry only on a book's newest failed job, the focused "queued again" line, refusal alert. `api.test.ts` `getJobs` (200, throws on 500). `JobsControllerTests`: a retried book ends with one copy per file, no `.partial`, the first job still `Failed`; re-approving a succeeded book is a 400 with no second job. Plus `AppLayout.test.tsx` nav order and `/jobs` current page |
| Manual: approve, then watch `/jobs` go to Succeeded without a reload, focus kept | Not run | API and client not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: failed job shows its errors; Retry after removing the blocker succeeds with one copy per file | Not run | Same |

Data checks: none (no schema change).

## Outcome (2026-10-19, commit aa9ee56)

**Status:** Implemented
**Summary:** A Jobs page (`/jobs`, in the nav after Preview) lists every job with status, times, its error and each file's destination, status and error, polling every 2 s while any job is queued or running. A book's newest failed job has a Retry button that approves the book again and announces "<title> is queued again." in a focused status. Backend tests prove a retry leaves one copy per file and that a succeeded book can't be approved twice.
**Changed:** 9 files: new `pages/JobsPage.tsx` and `JobsPage.test.tsx`; `services/api.ts`, `api.test.ts`, `App.tsx`, `components/layout/AppLayout.tsx`, `AppLayout.test.tsx`, `Tests/Controllers/JobsControllerTests.cs`; the slice file. No production backend change.
**Deviations:**
- No red runs: the backend retry tests confirm existing behaviour (as the slice predicted), and the Jobs page was written before its test was first run: *fine* (process note).
- After a successful Retry the button stays disabled until the reload removes it; a failed poll replaces the list with the alert; the empty-state link reads "Approve books on the Preview page": *fine*.
- Retrying two books with the same title in a row won't re-focus the status line (same text): *debt*, minor; phase 9 polish.
**Follow-ups:**
- Phase 8's only remaining done-when item is the Audiobookshelf-compatible custom fields (narrator, series, series part, ASIN). It needs the developer's answer to the open tag-frame question first (gate 1).
- Phase 9 polish: re-focus the Jobs status line on a repeat announcement.
