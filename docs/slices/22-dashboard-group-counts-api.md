# Slice 22: Dashboard group and file-state counts through a dashboard service (API)

**Phase:** 4, Group corrections
**Goal:** `GET /api/dashboard` also returns how many proposed books there are and how they break down by confidence and correction, plus how many files are unresolved or excluded. The query moves out of the controller into a `DashboardService` (the follow-up carried from slices 08 and 09).

## Done when
- [x] A new `Services/DashboardService.cs` builds the `DashboardDto`, and `DashboardController.Get` only calls it. The service is registered in `Program.cs` like the other services
- [x] `DashboardDto` keeps its five existing fields and adds:
  - `bookCount` (every stored group)
  - `booksByConfidence: { low, medium, high }`
  - `correctedBookCount` (`UserCorrected`)
  - `unresolvedFileCount` (the same files `GET /api/groups` lists as `unresolved`)
  - `excludedFileCount` (the same files it lists as `excluded`)
- [x] On a fresh database every new count is 0. After a scan, the counts match `GET /api/groups`, and they follow corrections such as a rename, an exclude or a send to unresolved
- [x] All four checks pass. The client still typechecks: the new fields are additive, and the UI comes in the next slice

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, Dapper in `Data/`)
- `docs/slices/09-dashboard-ui.md` Outcome: the follow-up to move the dashboard query into a service when group counts are added
- `docs/slices/20-exclude-and-unresolve-api.md` Outcome: `UserState`, and what `unresolved` and `excluded` contain
- `src/AudiobookOrganizer.Api/Controllers/DashboardController.cs`, `Models/Api/DashboardDto.cs`, `Program.cs`, `Data/GroupRepository.cs` (`GetGroups`, `GetUngroupedFiles`, `GetExcludedFiles`), `Data/ScanRepository.cs` (`CountFilesSeenIn`, `GetLatestCompletedRun`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/DashboardControllerTests.cs`

## Tasks
- [x] **Counts query.** Add `GroupRepository.CountGroups()`. It returns total, low, medium, high and corrected in one `SELECT` using `COUNT(*)` and `SUM(CASE ...)`, with `COALESCE` so an empty table gives zeros. Add `CountUnresolvedAndExcluded(scanId)`, which uses the same filters as `GetUngroupedFiles` and `GetExcludedFiles` but returns counts. Verify: the controller tests below.
- [x] **Service.** Add `Services/DashboardService.cs` (`SettingsService`, `ScanRepository`, `GroupRepository`) with `Get()`, which moves today's controller logic in and adds the new counts. The file counts use the latest completed scan, or 0 when there is none. Register it as a singleton in `Program.cs`. `DashboardController` takes `DashboardService` and returns `Get()`. Verify: the existing dashboard tests still pass unchanged.
- [x] **DTO.** Add `BooksByConfidenceDto(int Low, int Medium, int High)` and the new fields to `DashboardDto`, with the summary comment updated. Verify: the tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data: none (read-only counts over existing tables).
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the UI is the next slice).

## Tests to add
- `Controllers/DashboardControllerTests.cs`: on a fresh database, `bookCount`, every `booksByConfidence` value, `correctedBookCount`, `unresolvedFileCount` and `excludedFileCount` are 0.
- `Controllers/DashboardControllerTests.cs`: after a scan, `bookCount` and the confidence counts equal what `GET /api/groups` returns (count its groups by `confidence`), and `unresolvedFileCount` equals its `unresolved` length.
- `Controllers/DashboardControllerTests.cs`: after renaming a group and excluding one file through the groups API, `correctedBookCount` rises by the number of groups now `userCorrected`, and `excludedFileCount` is 1, with `unresolvedFileCount` unchanged or reduced to match `GET /api/groups`.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`). Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan.
  - `GET /api/dashboard` shows 2 books (1 High, 1 Medium), 0 corrected, 1 unresolved and 0 excluded.
  - Exclude the stray file through `POST /api/groups/excluded`: 0 unresolved, 1 excluded.
- Delete the scratch folders and database afterwards.

## Out of scope
- Showing the counts on the Dashboard page (next slice).
- Processing statuses (approved, processed, failed). They don't exist until phases 7 and 8, which will add their own counts.
- Filters by status and confidence (PRD 8). Filtering lives on the Incoming list and comes with those statuses.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 (reopened by the developer for "dashboard group and status counts, moving the dashboard query into a service") and the PRD's dashboard ("counts by status"). Today the only book states are confidence, corrected, unresolved and excluded, so those are the counts. It also fits AGENTS.md Layout (thin controller, logic in `Services/`). No `[NEEDS CLARIFICATION]` in scope.
- **Split:** the Dashboard page UI (`types/dashboard.ts`, `DashboardPage.tsx` and its test, and the `AppLayout.test.tsx` mock) is the next slice. Doing both would touch about 10 files.
- **Size:** 4 tasks, 6 files: `DashboardService.cs` (new), `DashboardController.cs`, `DashboardDto.cs`, `Program.cs`, `GroupRepository.cs`, `DashboardControllerTests.cs`.
- **Commit:** `feat: slice 22 dashboard-group-counts-api`.
