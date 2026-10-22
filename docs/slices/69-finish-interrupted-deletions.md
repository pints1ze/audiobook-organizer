# Slice 69: Finish interrupted deletions

**Phase:** 9, Hardening & release
**Goal:** at startup, a job cut off while deleting its sources (it has a `SourceDeleted` file, so every copy verified) finishes deleting the remaining unchanged sources, cleans up their rows and emptied folders, and is marked `Succeeded` instead of `Failed`.

## Done when
- [ ] at startup, a `Running` job with any `SourceDeleted` file deletes the source of each file still `Copied` with no error, marks it `SourceDeleted`, removes those files' `AudioFile`/`FileGroupFile` rows and the incoming folders left empty (slice 68's cleanup), and ends `Succeeded` with no error and a finish time
- [ ] a remaining source whose size or last write time differs from its `AudioFile` row (or whose row is gone), or that can't be deleted, is kept with `SourceChanged` / `SourceNotDeleted` as its file error, as a normal run does; the job still ends `Succeeded`
- [ ] with keep-source on at startup, such a job ends `Succeeded` and no remaining source is deleted
- [ ] every copy is kept; a `Running` job with no `SourceDeleted` file is still failed and cleaned up as in slice 65
- [ ] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`: `Run` (~96, calls `DeleteSources` with in-memory `SourceFacts`), `DeleteSources` (~280), `RemoveEmptyFolders` (~317), `FailInterruptedJobs` (~383)
- `src/AudiobookOrganizer.Api/Program.cs:79-81`: the startup call and its comment
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs`: `RemoveFiles` (slice 68) and how it queries `AudioFile`
- `src/AudiobookOrganizer.Api/Services/ScanService.cs:~100-115`: `AudioFile.SizeBytes` and `ModifiedAtUtc` (`LastWriteTimeUtc.ToString("O")`) and how a scan compares them
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs`: `At_startup_an_interrupted_job_with_a_deleted_source_keeps_every_copy` (~1011) and its helpers (`QueueOnly`, `PlaceFile`, `SetStatuses`, `Hashes`, `FileStatuses`, `WaitForJob`); `With_keep_source_off_a_processed_books_rows_and_emptied_folder_are_removed_and_its_job_history_stays` (~814) for row and folder asserts
- `docs/CURRENT-STATE.md`, Decisions dated 2026-10-22 (finish the deletions; slice 65 and 68 rules)

## Tasks
- [ ] Write the tests below in `JobsControllerTests.cs` first. Rework `At_startup_an_interrupted_job_with_a_deleted_source_keeps_every_copy` into the first new test (its "Failed, statuses unchanged" asserts are superseded by the 2026-10-22 decision, not weakened). Watch them fail.
- [ ] Add `GroupRepository.GetFiles(IReadOnlyList<long> ids)` returning those `AudioFile` rows (Dapper, `WHERE Id IN @ids`).
- [ ] In `ProcessingService`, let `DeleteSources` take each file's expected size and last write time instead of `SourceFacts` (e.g. a list of `(long Size, DateTime LastWriteUtc)?`, null meaning "unknown, keep"), and skip any file that isn't `Copied` or already has an error. `Run` passes its in-memory facts as before.
- [ ] In `FailInterruptedJobs`, for a `Running` job with a `SourceDeleted` file: unless keep-source is on, build the expected facts from `GetFiles` (`SizeBytes`, `ModifiedAtUtc` parsed round-trip as UTC; a missing row is null) and call `DeleteSources`; then mark the job `Succeeded` with no error and a finish time. Jobs without a `SourceDeleted` file keep the slice 65 path. Update the method's doc comment and the `Program.cs` comment to match.
- [ ] Run all four checks.
- Data: none (no schema change; rows and folders are removed as in slice 68)
- Auth: none
- Accessibility: none

## Tests to add
- `JobsControllerTests`: a `Running` job with file 1 `SourceDeleted` (source deleted, copy placed) and file 2 `Copied` (copy placed, source unchanged) ends `Succeeded` with no error at startup; both files `SourceDeleted`, file 2's source gone, both copies unchanged, both `AudioFile` rows gone, `incoming/alpha` removed and `incoming/beta` kept. Must be seen to fail first.
- `JobsControllerTests`: as above, but file 2's source is changed after the scan (append bytes): the job ends `Succeeded`, file 2 stays `Copied` with the `SourceChanged` error, and its source and `AudioFile` row stay.
- `JobsControllerTests`: as the first, with keep-source on before startup: the job ends `Succeeded`, file 2 stays `Copied` and its source stays.
- The existing slice 65 startup tests (copies removed, pending destination kept, only `Running` jobs change) and slice 68 cleanup tests still pass.

## Manual checks
- None beyond slice 65's: a stop timed exactly during source deletion can't be produced reliably by hand; the tests simulate it.

## Out of scope
- Resuming a job cut off before every copy verified (still failed cleanly, slice 65).
- Removing empty library folders left by failed jobs (the `RemoveCopies` shortcut).
- Any UI change: the Jobs page already shows `Succeeded` and per-file errors.

## Notes
- Conflict check: matches the 2026-10-22 decision ("an interrupted job that already has a `SourceDeleted` file finishes deleting the remaining sources and is marked `Succeeded`") and slice 57's rule that sources are deleted only after the whole job verified: a `SourceDeleted` file proves the verify pass completed. Supersedes slice 65's "only failed" behaviour for that case.
- Assumption: the change check at startup uses the scan's `AudioFile` facts, since the copy-time facts were in memory only. If a source changed between scan and copy, the copy holds the changed version and the source is kept anyway: the safe direction.
- Assumption: keep-source is read at startup, as `Run` reads it at the end of a run; turning it on before a restart keeps the remaining sources.
- Files touched: `ProcessingService.cs`, `GroupRepository.cs`, `Program.cs`, `JobsControllerTests.cs`, this slice file, PLAN.md (6).
- Remaining phase 9 work: Docker docs and the home-server run, and the other carried-in items in PLAN.md.
