# Slice 93: Drop vanished books

**Phase:** 11, Parallel identification
**Goal:** a rescan removes a user-corrected book whose files are all gone from incoming, so stale books stop showing up.

## Done when
- [x] after a completed scan, a user-corrected group none of whose files was seen in that scan is deleted (with its match, cover, metadata and stored candidates, which cascade), in the same transaction as the regroup
- [x] a user-corrected group with at least one file seen in the scan is kept unchanged, files and order included (partly missing books are out of scope)
- [x] a group with any `ProcessingJob` (queued, running, failed or succeeded) is never deleted this way, so processed books and job history stay
- [x] `GET /api/groups` and `GET /api/review` no longer list the removed book after the rescan
- [x] all four checks pass with no new warnings

## Read first
- `docs/CURRENT-STATE.md` Decisions (rescans keep corrections; slice 68 removes processed books' files)
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`ReplaceProposed`, `GetGroupableFiles`, `RemoveFiles`)
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingService.cs` (`Regroup`) and `Services/ScanService.cs` (`LastSeenScanId`, `FilesMissing`)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (the `FileGroup` foreign keys: which tables cascade, `ProcessingJob.GroupId` is `ON DELETE SET NULL`)
- `src/AudiobookOrganizer.Api.Tests/Services/GroupingServiceTests.cs` (temp incoming with fixtures, `NewScanService`, `Renamed_group_keeps_…_after_a_rescan`)

## Tasks
- [x] **Delete vanished corrected groups.** In `GroupRepository.ReplaceProposed`, inside its transaction and before the existing delete, also delete `FileGroup` rows with `UserCorrected = 1` that have no `FileGroupFile` whose `AudioFile.LastSeenScanId = @scanRunId` and no `ProcessingJob` row. Update the method's summary.
- [x] **Tests** (below), then all four checks.
- Data: no schema change; deletes rows through existing cascades (`FileGroupFile`, `Audiobook` and its match/metadata/cover, `CandidateSearch`/`MetadataCandidate`).
- Auth: none (no auth in the app).
- Accessibility: none (no UI).

## Tests to add
- `Api.Tests/Services/GroupingServiceTests.cs`: rename the "book a" group (user-corrected), delete its folder from the temp incoming, rescan: the group is gone from `List()` and from the database; the uncorrected M4B group is still listed.
- Same file: a corrected group with one of its two files deleted keeps its id, title and remaining file after the rescan.
- Same file: a corrected group whose files are all gone but which has a `ProcessingJob` row (insert a Failed job directly) is still there after the rescan.
- `Api.Tests/Controllers/ReviewControllerTests.cs` (or the existing review test that seeds a scan): a corrected book whose folder is deleted is gone from `GET /api/review` after a rescan.

## Manual checks
- Restart the dev API and rescan the real incoming: "Dont Let Him In" (group 65) and the 92-file "How to Break a Dragon's Heart" (group 498) are gone from Review and Incoming; the four current Dragon's Heart books stay.

## Out of scope
- Books with only some files missing (they stay; preview still reports their problems).
- Deleting the leftover image-only folders in incoming.
- Unreadable files (slice 94).

## Notes
- Developer request, 2026-10-26: added to phase 11 after a rescan kept corrected books whose folders had been moved away (595 missing files, groups 65 and 498).
- Conflict check: "rescans keep earlier corrections" (PRD item 2) still holds for any book with a file present; a book with none can't be processed anyway. Jobs keep their history because grouped books with jobs are skipped.
- A cancelled or failed scan doesn't regroup, so it never deletes anything.
- Files touched: `GroupRepository.cs`, `GroupingServiceTests.cs`, `ReviewControllerTests.cs`: 3.
- Later slice for this phase: 94, read files TagLibSharp can't parse through ffprobe and list failed files on Incoming.

## Test results (2026-10-26)

**Regressions:** none. During implementation one filtered run failed `GroupPreviewControllerTests.With_no_library_path_each_file_has_an_error_and_null_paths` (untouched by this slice): Failed (accepted) as a flake by the developer, 2026-10-26; it passed alone and in 3 full runs.

| Check | Result | Notes |
|---|---|---|
| Build | Passed | API and test project 0 warnings; `-p:OutDir=<scratchpad>` |
| Typecheck | Passed | |
| Backend tests | 446 passed, 0 failed | 2 full runs |
| Frontend tests | 376 passed, 1 failed | `JobsPage.test.tsx > focuses the status line again when a second book with the same title is queued`: Failed (accepted), the slice 78 flake; no client code changed |
| New tests | 4 of 4 present, passing | `GroupingServiceTests`: vanished corrected group deleted, M4B group kept; one file left keeps the group; a group with a job is kept. `ReviewControllerTests`: vanished corrected book leaves `GET /api/review` and `GET /api/groups` |
| Manual: rescan the real incoming, groups 65 and 498 gone | Not run | needs the dev API restarted; neither group has a job (checked `GET /api/jobs`) |

## Outcome (2026-10-26, commit 7f659f0)

**Status:** Implemented
**Summary:** `GroupRepository.ReplaceProposed` now also deletes, in the regroup's transaction, every user-corrected group with no job none of whose files was seen in the completed scan; its match, metadata, cover and candidates cascade. Partly missing books and books with any job stay.
**Changed:** 3 code files (`GroupRepository.cs`, `GroupingServiceTests.cs`, `ReviewControllerTests.cs`)
**Done when:** all 5 met; manual check not run.
**Deviations:**
- The tests rely on the existing `ON DELETE CASCADE` keys for the match, cover, metadata and candidates rather than checking each table: fine.
**Follow-ups:**
- Slice 94 (developer, 2026-10-26): scan files TagLibSharp can't parse through ffprobe; slice 95: process them by remuxing the MP3 copy with ffmpeg first.
