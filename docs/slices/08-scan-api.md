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
