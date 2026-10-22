# Slice 65: Interrupted job cleanup

**Phase:** 9, Hardening & release
**Goal:** a job cut off by a restart fails cleanly: at startup its copies and its `.partial` file are removed, so approving the book again processes it with no "already exists" refusal, and no source is ever put at risk.

## Done when
- [ ] at startup, each `Running` job with no `SourceDeleted` file is failed as interrupted, the destinations of its `Copied` files and the `.partial` of its first `Pending` file are deleted, and those `Copied` files go back to `Pending` (what a failed run already does through `RemoveCopies`)
- [ ] after that cleanup, approving the same book again queues it (preview clean) and the job runs to `Succeeded`
- [ ] a `Running` job with any `SourceDeleted` file keeps every copy: it's only failed as interrupted, because its sources may already be gone
- [ ] sources are never touched by the startup cleanup, and a `Pending` file's destination (which this job may not own) is never deleted
- [ ] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `src/AudiobookOrganizer.Api/Program.cs:76-82`: the startup calls for interrupted scans and jobs
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`: `Run` (~95–147, files run in position order, so the in-flight file is the first `Pending` one), `Copy` (the `.partial` is `destination + ".partial"`), `RemoveCopies` (~308)
- `src/AudiobookOrganizer.Api/Data/ProcessingRepository.cs`: `GetJobs`, `GetJob`, `FailInterruptedJobs` (~95)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs`: `QueueOnly` (~102), `Destinations`, `PartialFiles` (~128–140), `ScanTwoBooks`, `ApproveJob`, `WaitForJob`, and `At_startup_a_running_job_is_failed_as_interrupted_and_queued_jobs_run` (~713)
- `docs/CURRENT-STATE.md`, Decisions for slices 55 and 57 (a failed job removes only its own copies; sources deleted only after the whole job verified)

## Tasks
- [ ] In `JobsControllerTests.cs`, add the cleanup test: with a `QueueOnly` host, scan two books, approve book 1, then simulate a cut-off run: copy file 1's source to its destination (create the folder) and mark that file `Copied`, write a few bytes to file 2's `destination + ".partial"`, and set the job `Running` (SQL through `SqliteConnectionFactory`, as the existing interrupt test does). Start the worker host (`_factory.CreateClient()`): the job is `Failed` with the interrupted message, no destination or `.partial` exists, both sources are intact, and every job file is `Pending`. Then approve the book again: 201, and it runs to `Succeeded`. Watch it fail.
- [ ] In `ProcessingService`, add `FailInterruptedJobs()`: for each `Running` job (from `jobs.GetJobs()`), when no file is `SourceDeleted`, call `RemoveCopies(job, files, partial)` with `partial` = the first `Pending` file's `DestinationPath + ".partial"` (or null); then fail it with `Interrupted` and a finish time via `jobs.UpdateJob`. A job with a `SourceDeleted` file is only failed. Replace the repository call in `Program.cs:81-82` with this method and keep its comment accurate. Delete `ProcessingRepository.FailInterruptedJobs` if nothing else uses it, and move its test (`FailInterruptedJobs_fails_only_running_jobs`) to call the service method, so it still proves only `Running` jobs change. The first test goes green.
- [ ] Add the source-safety test: as above, but mark file 1 `SourceDeleted` (delete its source too) and file 2 `Copied` with its copy in place. After startup the job is `Failed` as interrupted, both copies still exist, file 2's source still exists, and the statuses are unchanged.
- [ ] Add the not-ours test: a `Running` job whose files are both `Pending`, with a file already at file 1's destination (written by the test, not the job). After startup that file still exists and the job is `Failed` as interrupted. Run all four checks.
- Data: none (no schema change; job file statuses change at startup as they do on a failed run)
- Auth: none
- Accessibility: none

## Tests to add
- `JobsControllerTests`: an interrupted job's `Copied` copies and in-flight `.partial` are removed at startup, its files go back to `Pending`, its sources stay, and approving the book again runs to `Succeeded` (must be seen to fail first).
- `JobsControllerTests`: an interrupted job with a `SourceDeleted` file keeps all its copies and the remaining source.
- `JobsControllerTests`: an interrupted job never deletes a file at a `Pending` file's destination.
- The existing `At_startup_a_running_job_is_failed_as_interrupted_and_queued_jobs_run` and the reworked `FailInterruptedJobs_fails_only_running_jobs` still pass.

## Manual checks
- With the dev API running, approve a multi-file book from a temp incoming copy and stop the API (Ctrl+C) while the Jobs page shows it `Running`. Restart: the job shows "Interrupted by an application restart.", the library has none of its files and no `.partial`, and Approve on the Preview page queues it again and it succeeds.

## Out of scope
- Resuming an interrupted job where it stopped (failing cleanly is the done-when's other option, and retry already re-runs the whole book).
- Finishing the source deletions of a job cut off after it verified (the `SourceDeleted` case); it stays failed with its copies kept. Open question below.
- Removing empty folders a job created (slice 55 shortcut) and any UI change.

## Notes
- Conflict check: matches PLAN.md phase 9's done-when "an interrupted job resumes or fails cleanly after restart" and the slice 55 rule that a failed job removes only its own copies. `RemoveCopies` is reused as is.
- Why a `Pending` file's destination is left: the run moves the copy into place before marking it `Copied` and reads it back in between, so a cut-off copy can be sitting there as `Pending`. The job can't tell it from a file someone else put there, so it's kept (`// shortcut:` comment), and a retry refuses it with "A file is already at …". The `.partial` is safe to delete because `Copy` refuses to start when one already exists.
- Open question for the developer (added to CURRENT-STATE.md at review): a job cut off while deleting sources has verified every copy; should startup finish the deletions and mark it `Succeeded` instead of failing it?
- Files touched: `ProcessingService.cs`, `ProcessingRepository.cs`, `Program.cs`, `JobsControllerTests.cs`, this slice file (5).
- Remaining phase 9 work: end-to-end fixture scan → process as one test, repeat scans after processing (deleted sources, empty incoming folders), Docker docs for the home server, and the carried-in items in PLAN.md.
