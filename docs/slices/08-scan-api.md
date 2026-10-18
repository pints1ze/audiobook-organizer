# Slice 08: Scan and dashboard API

**Phase:** 2, Settings & scan
**Goal:** a scan can be started over HTTP and runs in the background, one at a time, with progress readable while it runs. A dashboard endpoint returns the configured directories, the last scan and the inventory counts. A scan interrupted by a restart is marked failed, and files that failed before are retried.

## Done when
- [x] `POST /api/scans` returns 202 with the new run (`status: "Running"`) and the scan continues in the background after the response. A second `POST` while one is running returns 409 with a plain message
- [x] `GET /api/scans/latest` returns the most recent run with its counts, which update at least every 25 files while it runs, or 204 when there has never been a scan
- [x] `GET /api/dashboard` returns `incomingPath`, `libraryPath`, `lastScan` (latest run or null), `audioFileCount` (rows seen in the latest completed scan) and `filesWithErrors` (those rows with `ScanError`)
- [x] On startup, any `ScanRun` still `Running` is set to `Failed` with "Interrupted by an application restart."
- [x] A file with `ScanError` that is unchanged on disk is re-read on the next scan instead of being skipped
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, DTOs in `Models/Api/`), Environment (the interface stays responsive during scans; background work continues independently of the browser)
- `docs/slices/07-scan-inventory.md`, including its Outcome (retry debt)
- `src/AudiobookOrganizer.Api/Services/ScanService.cs`, `Data/ScanRepository.cs`, `Models/Local/ScanRun.cs`, `Data/DatabaseInitializer.cs`, `Program.cs`
- `src/AudiobookOrganizer.Api.Tests/Services/ScanServiceTests.cs` and `AudiobookOrganizerWebApplicationFactory.cs`

## Tasks
- [x] **Scan service fixes.**
  - In `ScanService`, treat an existing row with a non-null `ScanError` as changed, so it's re-read.
  - Persist the run's counts every 25 processed files through `repository.UpdateRun`, so progress is visible.
  - Add `ScanRepository.FailInterruptedRuns(message, now)`, called from `DatabaseInitializer.Initialize` (or right after it in `Program.cs`), which sets `Running` rows to `Failed` with a `FinishedAt`.

  Verify: the tests below.
- [x] **Background runner.** Add `Services/ScanRunner.cs` as a singleton. `TryStart(out ScanRun run)` takes a lock: if a scan is running it returns false; otherwise it inserts the run synchronously and starts `ScanService` on a background `Task`. Let `ScanService` accept an existing run, or split `Run` into start and execute. The task uses the `IHostApplicationLifetime.ApplicationStopping` token and logs any unexpected exception, marking the run `Failed`. No `Task.Run` fire-and-forget without exception handling. Verify: the tests below.
- [x] **Endpoints and DTOs.** Add:
  - `Models/Api/ScanRunDto.cs` (all `ScanRun` fields, camelCase) and `Models/Api/DashboardDto.cs`.
  - `Controllers/ScansController.cs`: `POST /api/scans` returns 202 with the run DTO and a `Location` of `/api/scans/latest`, or 409 `ProblemDetails`. `GET /api/scans/latest` returns 200 or 204.
  - `Controllers/DashboardController.cs`: `GET /api/dashboard`.
  - Repository queries for the latest run, the latest completed run and the two counts.

  Register them in `Program.cs`. Verify: the integration tests below.
- [x] **Tests.** Write the tests below. Integration tests set settings through `PUT /api/settings` with temp directories holding copies of the fixtures, then poll `GET /api/scans/latest` with a 10-second timeout until the run isn't `Running`. Verify: `dotnet test` is green.
- Data: no new tables. Startup now updates interrupted `ScanRun` rows. There are new read queries on `ScanRun` and `AudioFile`.
- Auth: none (no authentication by decision). `POST /api/scans` takes no body, so there's nothing to validate. It scans only the saved incoming directory.
- Accessibility: none (no UI in this slice).

## Tests to add
- `Controllers/ScansControllerTests.cs`: with incoming holding both fixtures, `POST /api/scans` returns 202 and `status: "Running"`. Polling `latest` then shows `Completed` with `filesFound: 2, filesAdded: 2`.
- `Controllers/ScansControllerTests.cs`: a `POST` while a scan is running returns 409. Make the run deterministic by holding it open; a test seam on `ScanRunner` or a slow fake `AudioFileReader` is fine. A `GET /api/scans/latest` on a fresh database returns 204.
- `Controllers/DashboardControllerTests.cs`: after a completed scan, `GET /api/dashboard` returns the saved paths, a `lastScan` with status `Completed`, `audioFileCount: 2` and `filesWithErrors: 0`. On a fresh database, `lastScan` is null and the counts are 0.
- `Services/ScanServiceTests.cs`: a corrupt file left unchanged on disk is re-read on the second scan. It isn't counted in `FilesUnchanged`, it's counted in `FilesFailed` again, and its `ScanError` is still set.
- `Data/` or `Services/` test: a `Running` row in the database is `Failed` with the interrupted message after `DatabaseInitializer.Initialize` runs again (or a new factory starts over the same file).

## Manual checks
- Run `scripts/start-api.ps1`, save real folders through the Settings page (or `PUT /api/settings`), and copy a few real audiobooks into incoming.
  - `curl -X POST http://localhost:5070/api/scans` returns 202. Repeated `curl http://localhost:5070/api/scans/latest` shows `Completed` with sensible counts, and `GET /api/dashboard` shows them too.
  - A second `POST` during a long scan returns 409. This needs enough files to take a few seconds; otherwise rely on the automated test.
- Delete the test folders afterwards and reset or delete the dev database. Report which.

## Out of scope
- The dashboard UI, the Scan button and polling in the client: slice 09.
- Cancelling a running scan from the UI.
- Scan history listing (only the latest is exposed).
- Group and status counts (phase 3 onward).

## Notes
- **Conflict check:** consistent with AGENTS.md (no new packages; background work continues independently of the browser), the PRD (manual scan, read-only) and `overview.md` section 12 (resume safely after a restart: interrupted runs are marked failed and a new scan can start). It resolves the slice 07 retry debt.
- **Split:** the planned "08 scan API and dashboard" was about 16 files, so it's split. This slice is the backend; slice 09 is the dashboard page (directories, last scan, counts, the "Scan incoming" button with progress polling, and the link to Settings when nothing's configured).
- **Size:** 4 tasks, about 11 files. Changed: `ScanService.cs`, `ScanRepository.cs`, `DatabaseInitializer.cs` (or `Program.cs`), `Program.cs`. New: `ScanRunner.cs`, `ScanRunDto.cs`, `DashboardDto.cs`, `ScansController.cs`, `DashboardController.cs`, 2 or 3 test files. That's above the ~8 guide, but the two DTOs and two controllers are thin pass-throughs.
- **Commit:** `feat: slice 08 scan-api`.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 59 passed, 0 failed | 10 new |
| Frontend tests | 20 passed, 0 failed | |
| New tests | 5 of 5 behaviours present, passing | plus 1 extra (`FailInterruptedRuns`) |
| Manual: fresh DB | Passed | `GET /api/scans/latest` returns 204; dashboard returns null paths, `lastScan: null`, 0 counts |
| Manual: scan of 62 files (1 M4B, 60 MP3s in `book one/disc 1/`, 1 corrupt) | Passed | `POST` returns 202 Running. Completed in about 3.6 s with found 62, added 61, failed 1 |
| Manual: second POST during scan | Passed | 409 "A scan is already running." |
| Manual: dashboard after scan | Passed | `audioFileCount: 62`, `filesWithErrors: 1`, `lastScan` Completed |
| Manual: rescan | Passed | About 0.05 s with 61 unchanged; the corrupt file was retried (failed 1), as intended |
| Manual: progress during scan | Partly | The `latest` call right after start showed 0 counts. The first 25-file save hadn't happened yet, and the scan finished before the next poll. Covered by the automated test |
| Manual: restart interruption | Not run | Timing is impractical by hand. Covered by the automated test |

**Data:** test folders and the dev database `data/audiobooks.db` deleted afterwards.

## Outcome (2026-10-18, commit 1c6d531)

**Status:** Implemented
**Summary:** This slice adds `ScanRunner`, which runs one background scan at a time on the app-stopping token and fails the run on unexpected errors. It adds `POST /api/scans` (202, or 409), `GET /api/scans/latest` (200, or 204), and `GET /api/dashboard`. Progress is saved every 25 files, interrupted runs are failed at startup, and previously failed files are retried. All Done when items are met.
**Changed:** 13 code files plus 3 docs. Key ones: `Services/ScanRunner.cs`, `Controllers/ScansController.cs`, `Controllers/DashboardController.cs`, `Services/ScanService.cs` (`Start` and `Execute`), `Data/ScanRepository.cs`.
**Deviations:**
- The dashboard query logic lives in `DashboardController` (one settings call plus three repository calls) rather than in a service. *Fine for now.* Move it to a service when phase 3 adds group counts.
- `AudioFileReader.Read` was made `virtual` for a gated test fake. *Fine.*
- While a scan is running, the dashboard counts can briefly drop, because unchanged files only get the new run id at the end. *Debt (cosmetic):* slice 09 should show the counts from the last *completed* run, labelled as such, while a scan runs.
**Follow-ups:**
- Slice 09: the dashboard UI. It uses `GET /api/dashboard`, polls `GET /api/scans/latest` while `Running`, and keeps showing the completed-run counts during a scan.
