# Slice 11: Persist groups after each scan

**Phase:** 3, Grouping
**Goal:** after every completed scan, the app runs the grouping engine over the files that scan saw and stores the proposed groups and their ordered files in SQLite. `GET /api/groups` returns the groups and the unresolved files.

## Done when
- [x] A completed scan leaves `FileGroup` and `FileGroupFile` rows that match the engine's result for the files seen in that scan. Files with a `ScanError` are excluded from grouping
- [x] A rescan replaces the previous groups, so there are no duplicates. If grouping throws, the previous groups stay intact (transaction rollback) and the run is marked `Failed` with "Grouping failed: …"
- [x] `GET /api/groups` returns 200 `{ groups: [...], unresolved: [...] }`:
  - Each group has `id`, `title`, `author`, `confidence` (`"High"`, `"Medium"` or `"Low"`), `evidence` (a string array), `fileCount` and `totalDurationMs`. Groups are ordered Low, then Medium, then High, then by title, so the ones needing review come first.
  - Each unresolved file has `fileId`, `relativePath` and `durationMs`.
  - With no scan, both lists are empty.
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, Dapper repositories in `Data/`, DTOs in `Models/Api/`, entities in `Models/Local/`), the file-safety rules (this slice only reads the database; no file system writes)
- `docs/slices/10-grouping-engine.md`, including its Outcome (the engine's rules and models)
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingEngine.cs` and `GroupingModels.cs`
- `src/AudiobookOrganizer.Api/Services/ScanService.cs` (`Execute` and where it finishes `Completed`), `Data/ScanRepository.cs`, `Data/DatabaseInitializer.cs`, `Program.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/ScansControllerTests.cs` (how integration tests set up incoming and wait for a scan)

## Tasks
- [x] **Schema and repository.** In `DatabaseInitializer`, add:
  - `FileGroup`: `Id` INTEGER PK, `Title`, `Author`, `Confidence` TEXT, `EvidenceJson` TEXT, `FileCount`, `TotalDurationMs`, `ScanRunId`.
  - `FileGroupFile`: `GroupId` (references `FileGroup` with ON DELETE CASCADE), `AudioFileId` UNIQUE (references `AudioFile`), `Position`, `Disc`, `Track`.

  Enable foreign keys on the connection (`PRAGMA foreign_keys = ON` in `SqliteConnectionFactory.Open`). Add `Data/GroupRepository.cs` with `ReplaceAll(IReadOnlyList<ProposedGroup>, long scanRunId)`, which deletes all groups and inserts the new ones in one transaction, plus the list queries for the API. Verify: the tests below.
- [x] **Grouping service and scan hook.** Add `Services/Grouping/GroupingService.cs` with `Regroup(long scanRunId)`.
  1. Load the `AudioFile` rows with `LastSeenScanId = scanRunId` and no `ScanError`.
  2. Map them to `GroupingInput`. `RelativePath` comes from `Path.GetRelativePath(incomingRoot, SourcePath)` with `\` replaced by `/`, and `Extension` from the row. The incoming root is the saved setting, normalised the same way `ScanService` does.
  3. Run `GroupingEngine.Group`, then `ReplaceAll`.

  In `ScanService.Execute`, call it after the files are processed and before finishing `Completed`. On an exception, finish the run as `Failed` with "Grouping failed: {message}" and log it. Mark the replace-all behaviour in code: `// shortcut: regroup replaces every group — revisit in phase 4 when user-confirmed groups must survive rescans`. Verify: the tests below.
- [x] **API.** Add `Models/Api/GroupDtos.cs` (`GroupSummaryDto`, `UnresolvedFileDto`, `GroupListDto`) and `Controllers/GroupsController.cs` with `GET /api/groups`. Unresolved files are the rows seen in the latest completed scan that have no `ScanError` and no `FileGroupFile` row. Register the new classes in `Program.cs`. Verify: the integration test below.
- [x] **Tests.** Write the tests below, using temp incoming directories with copies of the fixtures in scenario-like layouts, so the file system is real. Verify: `dotnet test` is green.
- Data: two new tables, created idempotently at startup, and foreign keys now enforced on every connection. Existing tables have no foreign keys, so the pragma doesn't affect them. The groups are fully replaced after each completed scan.
- Auth: none (no authentication by decision). `GET` only.
- Accessibility: none (no UI).

## Tests to add
- `Services/GroupingServiceTests.cs`: incoming holds `book a/track1.mp3, track2.mp3` (copies of the MP3 fixture) and `single.m4b`. After a scan there are 2 groups. The book-a group has files in positions 1 and 2, and the M4B group has 1 file.
- `Services/GroupingServiceTests.cs`: rescanning the same incoming leaves the same number of groups (no duplicates). A corrupt `broken.mp3` is in no group and isn't listed as unresolved.
- `Services/GroupingServiceTests.cs`: when grouping throws (e.g. a test seam or a repository fake that throws), the previous groups are still there and the run is `Failed` with an error starting "Grouping failed".
- `Controllers/GroupsControllerTests.cs`: on a fresh database, `GET /api/groups` returns empty lists. After a scan of two untagged root files with different names plus one M4B, it returns 1 group (High or Medium) and 2 unresolved files with their relative paths.
- `Controllers/GroupsControllerTests.cs`: ordering puts a Low group before a Medium one. Use a folder whose files repeat track numbers to get Low.

## Manual checks
- Run the API, save incoming and library to temp folders, and copy in the fixtures laid out like scenarios B and D (copies renamed as needed). Then `POST /api/scans`, wait, and `GET /api/groups`. The groups and unresolved files match what the engine tests promise. Delete the temp folders and the dev database afterwards.

## Out of scope
- The Incoming screen: slice 12.
- Group counts on the dashboard and moving the dashboard query into a service: a later phase 3 slice. They're carried in PLAN.
- Group detail (files per group), manual corrections and keeping user corrections across rescans: phase 4.
- Group status values (Discovered, Grouped, Needs Review…): set when phase 4 and 5 need them.

## Notes
- **Conflict check:** consistent with AGENTS.md (no new packages, layout, scans read-only), the PRD (groups proposed with confidence and evidence; unresolved files shown rather than silently assigned) and slice 10's models. Replacing all groups on each scan is correct while nothing is user-confirmed. The shortcut is marked for phase 4.
- **Risk:** enabling `PRAGMA foreign_keys` is global to every connection. Existing tables have no foreign keys, so nothing should change, but the full suite will confirm it.
- **Size:** 4 tasks, about 10 files: `DatabaseInitializer.cs`, `SqliteConnectionFactory.cs`, `GroupRepository.cs`, `GroupingService.cs`, `ScanService.cs`, `GroupDtos.cs`, `GroupsController.cs`, `Program.cs` and 2 test files. That's slightly over the guide; the DTO and controller files are thin.
- **Commit:** `feat: slice 11 persist-groups`.
- **Remaining in phase 3:** 12, the Incoming screen (client): a route and nav link, a list of groups and unresolved files, and the `AppLayout` test mock fix. Possibly 13: dashboard group counts via a `DashboardService`, plus grouping rule tweaks after the first real-collection run.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 85 passed, 0 failed | 6 new |
| Frontend tests | 40 passed, 0 failed | |
| New tests | 5 of 5 behaviours present, passing | plus 1 extra (a `ReplaceAll` foreign-key failure after the delete rolls back) |
| Manual: scenario layouts through the real API | Passed | 15 files: scenario B folder, scenario C disc folders, scenario D root series x2, scenario A M4B, one stray root MP3. Result: 5 groups (all Medium with folder, disc-folder or filename evidence) and `random thing.mp3` unresolved. A rescan keeps 5 groups (no duplicates). `GET /api/groups` on a fresh DB returns empty lists |

Observation: the fixture copies carry the artist tag "Fixture Author", so the estimated author comes from the tag rather than the folder ("ernest cline"). That's the rule as designed (tags win over folder names), but worth checking against real collections in slice 12.

**Data:** test folders and the dev database deleted afterwards. Verification query: `SELECT g.Title, g.Confidence, COUNT(f.AudioFileId) FROM FileGroup g JOIN FileGroupFile f ON f.GroupId = g.Id GROUP BY g.Id;`

## Outcome (2026-10-18, commit 0b8b97a)

**Status:** Implemented
**Summary:** This slice adds the `FileGroup` and `FileGroupFile` tables (foreign keys now enforced), `GroupRepository.ReplaceAll` (one transaction), and `GroupingService.Regroup`, which is called after each completed scan. A grouping failure marks the run `Failed` and keeps the earlier groups. It also adds `GET /api/groups` with groups ordered Low, then Medium, then High, then title, plus the unresolved files. All Done when items are met.
**Changed:** 12 code files plus 2 docs. Key ones: `Services/Grouping/GroupingService.cs`, `Data/GroupRepository.cs`, `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`, `Data/DatabaseInitializer.cs`, `Data/SqliteConnectionFactory.cs`, `Services/ScanService.cs`.
**Deviations:**
- `PRAGMA foreign_keys = ON` also starts enforcing the existing `AudioFile` reference to `ScanRun`. The slice wrongly said there was none. *Fine*: the full suite passes, and the references are always valid.
- `ScanServiceTests.cs` was touched for the new constructor parameter, and `Models/Local/FileGroup.cs` was added. *Fine*, by the repository's layout conventions.
- `GroupingService.Regroup` is `virtual` as a test seam. *Fine.*
- Unresolved paths are made relative to the *current* incoming setting, so if incoming changes after a scan they show `../`. *Debt (minor)*: it fixes itself on the next scan.
**Follow-ups:**
- Phase 4: the replace-all shortcut must give way to keeping user-confirmed groups (marked in code).
