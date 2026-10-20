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
