# Slice 55: Background runner copies queued books

**Phase:** 8, Processing
**Goal:** a background worker takes queued jobs one at a time and copies each file to its snapshotted destination without ever overwriting, so `GET /api/jobs/{id}` goes `Queued` → `Running` → `Succeeded` or `Failed` with the reason. Sources are never touched.

## Done when
- [x] after `POST /api/jobs`, the job is picked up without another request; each file is copied to its destination (folders created as needed) and becomes `Copied`, and the job becomes `Succeeded` with `startedAtUtc` and `finishedAtUtc` set
- [x] each copy is written under a temporary name in the destination folder and moved into place only when complete and the same size as the source; a destination that exists when its turn comes is never overwritten: that file is `Failed` with "A file is already at <destination>." and the job `Failed`
- [x] when a job fails, the copies it already made and any temporary file are removed, the source files are unchanged, and the next queued job still runs
- [x] a missing source fails the job with "The source file is gone: <path>."; a job left `Running` by a restart is marked `Failed` with "Interrupted by an application restart." at startup, and jobs still `Queued` are run
- [x] the incoming files are byte-for-byte unchanged after every job; all four checks pass

## Read first
- `docs/slices/54-approve-book-queue.md` and its Outcome
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`, `Data/ProcessingRepository.cs`, `Models/Local/ProcessingJob.cs`, `Controllers/JobsController.cs`
- `src/AudiobookOrganizer.Api/Services/ScanRunner.cs` and `Program.cs` (startup `FailInterruptedRuns`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs` (book setup helpers) and `Services/ScanServiceTests.cs` (`FailInterruptedRuns_fails_only_running_rows`)

## Tasks
- [x] **Repository and statuses.** `ProcessingJob.cs`: file statuses `Copied` and `Failed`. `ProcessingRepository`: `NextQueued()` (oldest `Queued` job with its files, or null), `UpdateJob(job)`, `UpdateFile(file)`, and `FailInterruptedJobs(message, finishedAt)` (`Running` → `Failed`, like `FailInterruptedRuns`).
- [x] **Copy step.** `ProcessingService.Run(jobId, CancellationToken)`: mark the job `Running` with `StartedAtUtc`; for each file in position order: source missing → fail; destination exists (file or folder) → fail; otherwise create the folder, copy to `<destination>.partial` (fail if that exists too, never overwrite it), check the size matches the source, `File.Move(temp, destination, overwrite: false)`, mark `Copied`. Any exception fails the file and job with its message. On failure delete only the destinations this job marked `Copied` and its own `.partial` file; leave created folders (shortcut: empty folders may remain — revisit in phase 9 cleanup). Finish with `Succeeded` or `Failed` and `FinishedAtUtc`; the job's `Error` is the failed file's error.
- [x] **Worker.** New `Services/ProcessingWorker.cs`, a `BackgroundService` registered as a singleton and as the hosted service in `Program.cs`. It drains the queue (`NextQueued` → `Run`) when it starts and whenever `Wake()` is called; a wake that arrives while it's draining isn't lost (e.g. a `SemaphoreSlim` released at most once while unsignalled). One job at a time; an unexpected exception is logged, the job marked `Failed`, and the loop continues. Stops on `stoppingToken`. `JobsController` calls `Wake()` after a 201. `Program.cs` calls `FailInterruptedJobs` at startup next to `FailInterruptedRuns`.
- [x] **Tests** (below). Run all four checks with full backend output (TRX).
- Data: none (new statuses only; the slice 54 tables are unchanged).
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- `JobsControllerTests`: approving a clean two-file book ends `Succeeded` (poll `GET /api/jobs/{id}` with a timeout); both destinations exist with the sources' bytes, the sources are unchanged, and no `.partial` file is left.
- `JobsControllerTests`: a file placed at the second file's destination after approval fails the job with "A file is already at …", that file keeps its own contents, the first copy is removed, and the sources are unchanged.
- `JobsControllerTests`: two approved books where the first fails (existing destination) and the second succeeds: the second still runs.
- `JobsControllerTests`: a source deleted after approval fails the job with "The source file is gone: …".
- `ProcessingRepository` test (e.g. `Services/ProcessingRepositoryTests.cs` or in `JobsControllerTests`): `FailInterruptedJobs` fails only `Running` jobs and leaves `Queued` and finished ones alone.

## Manual checks
- With the API running, approve a clean scanned book (`POST /api/jobs`): within seconds `GET /api/jobs/{id}` shows `Succeeded`, the files are in the library under the preview's paths, and incoming still has the originals.
- Stop the API while a large book is copying (or with a queued job), start it again: a cut-off job shows `Failed` "Interrupted by an application restart."; a queued one runs.

## Out of scope
- Writing tags and the cover, read-back verification, deleting sources (next slices); until then a `Succeeded` job is only a verified copy
- Retry, cancel, the jobs UI and an Approve button
- File ownership on the mounts (PUID/PGID, carried in phase 8) and cleaning up empty folders

## Notes
- File count: 7 (`ProcessingJob.cs`, `ProcessingRepository.cs`, `ProcessingService.cs`, new `ProcessingWorker.cs`, `JobsController.cs`, `Program.cs`, `JobsControllerTests.cs`), 8 with a separate repository test file. At the cap.
- Why next: slice 54's follow-up. The copy is the foundation the tag, verify and delete steps act on, and it carries the PRD rules "never overwrite" and "a failed book doesn't stop the others".
- Removing a failed job's copies keeps retry simple (the next slice can rerun from scratch without duplicates) and only ever deletes files this job wrote.
- `File.Move` across folders on the same volume is a rename; the temp file sits in the destination folder, so the move never crosses volumes (incoming and library may differ).
- Tests that wait on the worker poll with a timeout (e.g. 10 s) rather than sleeping a fixed time.
- Conflict check: consistent with AGENTS.md file safety (scans read-only, never overwrite, sources untouched), the PRD rules and slice 54's snapshot decision. The tag-frame open question isn't touched. No new packages.

## Test results (2026-10-19)

**Regressions:** none. A frontend flake predating this slice (`BookPage.test.tsx` "shows the Cover, Metadata, People and Genres sections…", 2 of 9 runs: `getBookPeopleMock` not yet called) was fixed with the developer's OK in `93524ee` (the call assertions now `waitFor`); 10 of 10 full runs passed afterwards.

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 353 passed, 0 failed | Was 347. Full output kept (TRX). `JobsControllerTests` also passed 6 extra runs in a row during implementation |
| Frontend tests | 249 passed, 0 failed | Unchanged count; flake fixed (above) |
| New tests | 5 of 5 present, passing | `JobsControllerTests`: copy succeeds with sources' bytes and no `.partial`; existing destination never overwritten and first copy removed; a failed job doesn't stop the next; source gone fails the job; `FailInterruptedJobs` fails only `Running`. Plus a startup test: interrupted `Running` failed, `Queued` run |
| Manual: approve a clean book, `Succeeded`, files in library, originals in incoming | Not run | API not running. Start with `scripts/start-api.ps1` |
| Manual: stop the API mid-job, restart: interrupted job `Failed`, queued job runs | Not run | Same |

Data checks (for the developer): `SELECT Id, Status, Error, StartedAtUtc, FinishedAtUtc FROM ProcessingJob ORDER BY Id DESC;` and `SELECT JobId, Position, Status, Error FROM ProcessingJobFile ORDER BY JobId DESC, Position;`

## Outcome (2026-10-19, commit 54767ea)

**Status:** Implemented
**Summary:** A hosted `ProcessingWorker` drains the queue at startup and whenever an approval wakes it, running one job at a time. `ProcessingService.Run` copies each file through a `.partial` file (`FileMode.CreateNew`) in the destination folder, checks the size and renames it into place without overwriting. Any problem fails the file and job with the reason and removes that job's copies. A job cut off by a restart is failed as interrupted at startup. Sources are only read.
**Changed:** 7 files: new `Services/ProcessingWorker.cs`; `ProcessingService.cs` (`Run`, `Fail`), `ProcessingRepository.cs` (`NextQueued`, `UpdateJob`, `UpdateFile`, `FailInterruptedJobs`), `ProcessingJob.cs` (`Copied`, `Failed`), `JobsController.cs` (wake after 201), `Program.cs`, `JobsControllerTests.cs` (+6, and a `QueueOnly()` host without the worker). Plus `93524ee`, the BookPage test fix.
**Deviations:**
- The copies a failed job removes go back to `Pending`, so no file claims a copy that isn't there: *improvement*.
- A shutdown between files fails the job as interrupted and cleans up its copies: *improvement*.
- The temp file is created with `CreateNew` and streamed, not `File.Copy`, so a half-written temp is provably this job's: *improvement*.
- Extra messages: "A file is already at <dest>.partial.", a size-mismatch message, and "Processing failed unexpectedly: …" from the worker: *fine*.
- `ProcessingService` takes a logger (for failed cleanup deletes), and the `FailInterruptedJobs` test sits in `JobsControllerTests`: *fine*.
- The BookPage flake fix is a separate `fix:` commit outside the slice, with the developer's OK: *fine*.
**Follow-ups:**
- Next: write tags and the chosen cover into each copy (MP3 through TagLibSharp, M4B through ffmpeg `-c copy`; track = book position) before it moves into place, with the standard fields first. The Audiobookshelf custom fields (narrator, series, ASIN) hang on the open tag-frame question.
- Then read-back verification, deleting sources unless keep-source, retry without duplicates, and the jobs UI with an Approve button.
- If the worker's last-resort `Fail` runs after an unexpected exception, that job's copies aren't removed; worth covering when retry lands.
