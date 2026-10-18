# Slice 05: Directory settings API

**Phase:** 2, Settings & scan
**Goal:** the API stores the incoming directory, the library directory and the "keep source files" toggle in SQLite. It validates them before saving and exposes them at `GET` and `PUT /api/settings`.

## Done when
- [x] `GET /api/settings` returns `{ incomingPath, libraryPath, keepSourceFiles }`. The paths are `null` and the toggle is `false` until something is saved
- [x] `PUT /api/settings` with valid paths saves them (a following `GET` returns them) and responds 200 with the saved values
- [x] `PUT /api/settings` with an invalid value responds 400 `ValidationProblemDetails`, with errors keyed `incomingPath` / `libraryPath`, and saves nothing
- [x] Settings survive an API restart (they're stored in the SQLite `ApplicationSetting` table)
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (Controllers stay thin, logic goes in `Services/`, Dapper repositories in `Data/`, DTOs in `Models/Api/`), Environment (dev paths are Windows, container paths are Linux; incoming must never be modified by validation)
- `docs/PRD.md`: workflow 1 (Configure) and Rules ("Only the configured directories may be read or written")
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs`, `Data/SqliteConnectionFactory.cs`, `Program.cs`
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizerWebApplicationFactory.cs` (each factory gets its own temp database)

## Tasks
- [x] **Schema and repository.** In `DatabaseInitializer.Initialize`, create the table with `CREATE TABLE IF NOT EXISTS ApplicationSetting (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)`. Add `Data/SettingsRepository.cs` with `Get(key)` and `SetMany(dictionary)`. `SetMany` upserts with `INSERT ... ON CONFLICT(Key) DO UPDATE` inside one transaction. Keys are `IncomingPath`, `LibraryPath` and `KeepSourceFiles` (stored as `"true"`/`"false"`). Verify: the tests below.
- [x] **Validation and service.** Add `Services/SettingsService.cs` with `Get()` and `Save(request)`. `Save` returns either the saved settings or a field-error dictionary.
  - Rules for each path: required; absolute (`Path.IsPathFullyQualified`); exists and is a directory; readable (enumerate one entry).
  - Rules for the library only: writable, checked by creating and deleting a uniquely named probe file. **Never write anything into incoming.**
  - The two paths can't be the same, and neither can sit inside the other. Compare normalized full paths with a trailing separator, case-insensitive on Windows and case-sensitive elsewhere.
  - Error messages are plain sentences the UI can show as they are.

  Verify: the tests below.
- [x] **Controller and DTO.** Add `Models/Api/SettingsDto.cs` (`IncomingPath`, `LibraryPath`, `KeepSourceFiles`) and `Controllers/SettingsController.cs`, a thin wrapper over the service. `GET /api/settings` returns 200. `PUT /api/settings` returns 200 with the saved DTO, or `ValidationProblem` (400) with camelCase field keys. Register the repository and service in `Program.cs`. Verify: the integration tests below.
- [x] **Tests.** Write the tests below, with temp directories created and deleted per test. Verify: `dotnet test` is green.
- Data: a new `ApplicationSetting` table, created idempotently at startup by `DatabaseInitializer`. There's no migration framework; later slices add `CREATE TABLE IF NOT EXISTS` statements the same way.
- Auth: none (no authentication by decision). The trust boundary is the request body, so every field is validated server-side.
- Accessibility: none (no UI in this slice).

## Tests to add
- `Services/SettingsServiceTests.cs`: valid incoming and library temp directories save. The library probe file is gone afterwards, and the incoming directory's contents are unchanged.
- `Services/SettingsServiceTests.cs`: missing, relative, non-existent and file-not-directory paths each give an error on the right field.
- `Services/SettingsServiceTests.cs`: the same directory for both, library inside incoming, and incoming inside library are each rejected.
- `Controllers/SettingsControllerTests.cs`: `GET` returns nulls and `false` on a fresh database. A valid `PUT` followed by `GET` round-trips the values.
- `Controllers/SettingsControllerTests.cs`: an invalid `PUT` returns 400 with an `errors.libraryPath` (or `errors.incomingPath`) entry, and a following `GET` still returns the previous values.

## Manual checks
- With `scripts/start-api.ps1` running:
  - `curl -X PUT http://localhost:5070/api/settings -H "Content-Type: application/json" -d '{"incomingPath":"C:\\temp\\in","libraryPath":"C:\\temp\\lib","keepSourceFiles":false}'`, using real folders, returns 200.
  - A bad path returns 400 with a readable message.
  - Restart the API and `GET` still returns the saved values.
- Delete the test folders afterwards.

## Out of scope
- The settings page, layout shell, navigation and not-found route: slice 06.
- Naming templates (phase 7) and metadata provider settings (phase 5).
- Defaulting the paths to `/incoming` and `/library` in Docker. The user enters them once. Revisit if that's annoying.
- Scanning: a later phase 2 slice.

## Notes
- **Conflict check:** consistent with AGENTS.md (layout, Dapper, no new packages), the PRD (validated directories, incoming never modified) and CURRENT-STATE (no auth; the "keep source files" toggle defaults to off). The library write probe is the one write the app makes before processing. It's the minimum needed to prove the library is writable and leaves nothing behind.
- **Split:** the original "directory settings" piece (API plus page plus layout) is about 14 files, so it's split. This slice is the API; slice 06 is the layout shell with navigation and not-found (carried from slice 03) plus the settings page.
- **Size:** 4 tasks, 8 files: `DatabaseInitializer.cs`, `SettingsRepository.cs`, `SettingsService.cs`, `SettingsController.cs`, `SettingsDto.cs`, `Program.cs` and 2 test files.
- **Commit:** `feat: slice 05 directory-settings-api`.
- **Remaining in phase 2 after 06:** scan (the inventory table, `ScanRun`, recursive MP3/M4B discovery through TagLibSharp and `ChapterReader`, the unchanged-file skip on rescan, unreadable files recorded rather than fatal); dashboard (last scan and counts, a Scan button).

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 40 passed, 0 failed | 22 new |
| Frontend tests | 6 passed, 0 failed | |
| New tests | 5 of 5 behaviours present, passing | `SettingsServiceTests`, `SettingsControllerTests` (incl. restart over same DB file) |
| Manual: GET on fresh dev DB | Passed | `{"incomingPath":null,"libraryPath":null,"keepSourceFiles":false}` |
| Manual: valid PUT | Passed | 200 with the saved values; library left empty (probe deleted); incoming untouched |
| Manual: library nested in incoming (existing folder) | Passed | 400 `errors.libraryPath`: "The library directory can't be inside the incoming directory." The nested folder stayed empty |
| Manual: relative and empty paths | Passed | 400 with errors on both fields |
| Manual: values kept after invalid PUT | Passed | |
| Manual: API restart | Passed | the values are still returned |

**Data:** `data/audiobooks.db` (the dev database) now holds settings that point at deleted scratch folders. Re-save from the settings page (slice 06), or delete the git-ignored file to reset. Verification query for the developer: `SELECT Key, Value FROM ApplicationSetting;`

## Outcome (2026-10-18, commit d067bde)

**Status:** Implemented
**Summary:** This slice adds the `ApplicationSetting` table (created idempotently at startup), `SettingsRepository` (a transactional upsert), `SettingsService` and the thin `SettingsController` at `GET`/`PUT /api/settings`. The service checks each path is required, absolute, an existing directory and readable. It rejects same or nested paths and probes that the library is writable. It never writes into incoming. All Done when items are met.
**Changed:** 8 code files plus 3 docs. Key ones: `Services/SettingsService.cs`, `Data/SettingsRepository.cs`, `Controllers/SettingsController.cs`, `Data/DatabaseInitializer.cs`.
**Deviations:**
- The overlap check runs before the write probe, so the probe can never land in incoming. *Improvement.*
- All overlap errors, including "incoming inside library", are keyed `libraryPath`. *Fine*: the UI shows the message on the library field.
- The implementer didn't run the manual checks; this test step ran them. *Fine.*
**Follow-ups:**
- Slice 06: the settings page shows these field errors and has a layout shell with navigation and a not-found route.
