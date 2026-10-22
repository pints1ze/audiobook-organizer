# Slice 68: Processed source cleanup

**Phase:** 9, Hardening & release
**Goal:** once a processed book's sources are deleted, their `AudioFile` rows go and the incoming folders they leave empty are removed, so rescans stop counting them as missing.

## Done when
- [x] after a job succeeds with keep-source off, every `SourceDeleted` file's `AudioFile` row (and its `FileGroupFile` row) is gone; the `ProcessingJob` and `ProcessingJobFile` history is untouched
- [x] each source's folder, and each parent folder up to (never including) the incoming root, is deleted when it's left empty; a folder holding anything else (another book, a cover image, a file the job kept) stays
- [x] a rescan after processing reports `filesMissing` 0 for the processed book
- [x] with keep-source on, or for a source kept because it changed or couldn't be deleted, the row and folder stay
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`: `Run` (~96) and `DeleteSources` (~279)
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`FileGroupFile` writes) and `Data/DatabaseInitializer.cs` (~33 `AudioFile`, ~74 `FileGroupFile.AudioFileId REFERENCES AudioFile` without cascade; `ProcessingJobFile.AudioFileId` has no foreign key)
- `src/AudiobookOrganizer.Api/Services/ScanService.cs` (~66: `FilesMissing` counts known rows not seen)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs`: `ScanTwoBooks` (books in `incoming/alpha` and `incoming/beta`), the direct `SqliteConnectionFactory` queries (~261), and the slice 66 keep-source-off rescan test (~743 asserts `filesMissing` 2)
- `docs/CURRENT-STATE.md`, Decisions dated 2026-10-22

## Tasks
- [x] Write the tests below in `JobsControllerTests.cs` first and watch them fail.
- [x] Add `GroupRepository.RemoveFiles(IReadOnlyList<long> fileIds)`: in one transaction, delete the files' `FileGroupFile` rows, then their `AudioFile` rows. Nothing else is touched (the group stays, hidden as processed).
- [x] At the end of `ProcessingService.DeleteSources`, call `RemoveFiles` with the ids of the files now `SourceDeleted`, then remove empty folders: for each distinct source directory, walk up while the directory is strictly under the saved incoming path (compare full paths, ignoring case on Windows only through `Path.GetRelativePath` not starting with `..`) and has no entries, deleting it. A failure logs a warning and stops that walk; it never fails the job. Skip folder cleanup when the incoming path isn't set or the source isn't under it.
- [x] Update the slice 66 keep-source-off rescan test: its rescan now expects `filesMissing` 0 instead of 2, following the 2026-10-22 decision (not a weakened test). Run all four checks.
- Data: no schema change; rows are deleted in a transaction
- Auth: none
- Accessibility: none

## Tests to add
- `JobsControllerTests`: with keep-source off, after book 1 is processed its `AudioFile` and `FileGroupFile` rows are gone (direct SQL count), its job's files are still listed by `GET /api/jobs/{id}`, `incoming/alpha` is gone, `incoming/beta` and the incoming root remain, and a rescan reports `filesMissing` 0.
- `JobsControllerTests`: a non-audio file (e.g. `cover.jpg`) in book 1's folder keeps the folder (only the MP3s go).
- `JobsControllerTests`: with keep-source on, book 1's rows and folder stay after processing.
- `JobsControllerTests`: a book nested two folders deep (`incoming/author/title/`) removes both folders when they end up empty.

## Manual checks
- In the dev app with keep-source off, process one book from a temp incoming copy: its incoming folder disappears, the dashboard's missing count stays 0 after "Scan incoming", and the Jobs page still shows the job's files.

## Out of scope
- Finishing a cut-off job's source deletions at startup (decided 2026-10-22; next slice, which can reuse this cleanup).
- Cleaning up empty library folders left by failed jobs (the `RemoveCopies` shortcut).
- The processed book's page: with its files' rows gone it lists no files or embedded covers; it's hidden from the lists already (slice 67).

## Notes
- Conflict check: matches the 2026-10-22 decision ("once its sources are deleted, their `AudioFile` rows and the incoming folders left empty are removed"). Scans stay read-only: only processing deletes, and only folders it emptied. A source outside the incoming path (settings changed after approval) keeps its folder.
- Assumption: a group whose files are all removed is kept (it carries the book's metadata, cover and job link) and stays hidden.
- Files touched: `GroupRepository.cs`, `ProcessingService.cs`, `JobsControllerTests.cs`, this slice file, PLAN.md (5).
- Remaining phase 9 work: finishing cut-off source deletions at startup, Docker docs and the home-server run, and the other carried-in items in PLAN.md.

## Test results (2026-10-22)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 395 passed, 0 failed | |
| Frontend tests | 271 passed, 0 failed | the accepted `JobsPage` Retry flake didn't occur |
| New tests | 4 of 4 present, passing | rows and emptied folder removed with job history kept and rescan `filesMissing` 0; a `cover.jpg` keeps the folder; keep-source on keeps rows and folder; nested `author/title` folders both removed |
| Manual: process a book with keep-source off in the dev app; folder gone, missing count 0 after rescan, Jobs page keeps the files | Not run | dev API not running; start with `scripts/start.ps1` |

## Outcome (2026-10-22, commit 97068af)

**Status:** Implemented
**Summary:** When a job's sources are deleted, `ProcessingService.DeleteSources` now removes the `SourceDeleted` files' `FileGroupFile` and `AudioFile` rows (`GroupRepository.RemoveFiles`, one transaction) and deletes each source folder and parent left empty, stopping before the incoming root. Job history is untouched, and a rescan reports no missing files for the processed book.
**Changed:** 5 files: `Data/GroupRepository.cs` (`RemoveFiles`), `Services/ProcessingService.cs` (`RemoveEmptyFolders`, `IsStrictlyUnder`), `Tests/Controllers/JobsControllerTests.cs` (4 new tests; the slice 66 rescan now expects `filesMissing` 0), the slice file, and `scripts/start.ps1`.
**Deviations:**
- `scripts/start.ps1` was added by the developer during the slice and committed with the feat commit; the old start scripts were removed and AGENTS.md updated in `4a4fc52`: *fine*.
**Follow-ups:**
- No test covers a source kept because it changed or couldn't be deleted keeping its row; the code removes only `SourceDeleted` files.
- Finishing a cut-off job's source deletions at startup (2026-10-22 decision) can reuse this cleanup.
- Manual (developer): process a book with keep-source off in the dev app; its folder goes, the missing count stays 0 after a rescan, the Jobs page keeps its files.
