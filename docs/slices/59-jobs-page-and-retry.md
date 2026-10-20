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
