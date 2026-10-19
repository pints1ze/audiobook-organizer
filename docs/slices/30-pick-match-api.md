# Slice 30: Pick a match (API)

**Phase:** 5, Metadata
**Goal:** the API stores the catalog candidate the user picks for a book as its `Audiobook` and `MetadataMatch`, returns it, and keeps it across rescans.

## Done when
- [x] `PUT /api/groups/{id}/match` with a `MetadataCandidateDto` body returns 200 and a `BookMatchDto` (`groupId`, `match` = the stored candidate, `pickedAtUtc`). It creates the group's `Audiobook` if it has none, and replaces the book's earlier match if it had one (one current match per book).
- [x] `GET /api/groups/{id}/match` returns 200 with the `BookMatchDto`, or 204 when the book has no match. Both endpoints return 404 for an unknown group id.
- [x] Bad candidates return a 400 validation problem and change nothing: provider not `audible` or `openlibrary`, blank title, any text field over `GroupingService.MaxTextLength`, more than 50 authors or narrators, a cover URL that isn't absolute `http`/`https`, a negative runtime.
- [x] Picking a match marks the group `UserCorrected`, so a rescan of unchanged files keeps the group, its files and its match.
- [x] Deleting a group (merge or moving all its files out) removes its `Audiobook` and `MetadataMatch` by cascade, with no orphan rows.
- [x] All four checks pass.

## Read first
- `docs/slices/29-book-metadata-search.md`, `## Outcome`
- `docs/PRD.md`, Data (Audiobook "has one FileGroup"; MetadataMatch "a provider candidate, its score and the raw values retrieved") and Rules
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (the `CREATE TABLE IF NOT EXISTS` pattern, foreign keys) and `Data/GroupRepository.cs` (`UpdateTitleAuthor`, `ReplaceProposed`)
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingService.cs` (`Rename`, `CleanTitleAuthor`, `MaxTextLength`, the result-record pattern)
- `src/AudiobookOrganizer.Api/Controllers/GroupsController.cs`, `Models/Api/MetadataDtos.cs`, `Program.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupsControllerTests.cs` (factory setup, scanning a temp incoming folder), `Data/DatabaseInitializerTests.cs`

## Tasks
- [x] **Schema.** In `DatabaseInitializer.cs` add, after `FileGroupFile`:
  - `Audiobook (Id INTEGER PRIMARY KEY AUTOINCREMENT, GroupId INTEGER NOT NULL UNIQUE REFERENCES FileGroup (Id) ON DELETE CASCADE, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL)`
  - `MetadataMatch (Id INTEGER PRIMARY KEY AUTOINCREMENT, AudiobookId INTEGER NOT NULL UNIQUE REFERENCES Audiobook (Id) ON DELETE CASCADE, Provider TEXT NOT NULL, ProviderId TEXT, CandidateJson TEXT NOT NULL, PickedAtUtc TEXT NOT NULL)`. `ProviderId` is the ASIN, else the ISBN. `CandidateJson` is the candidate as received, the "raw values" for the editor slice.

  Add `Models/Local/Audiobook.cs` (the `Audiobook` and `MetadataMatch` rows). Verify: `DatabaseInitializerTests` (tables exist; initializing twice is harmless).
- [x] **Repository.** Add `Data/AudiobookRepository.cs`: `GetMatch(groupId)` and `SaveMatch(groupId, provider, providerId, candidateJson, nowUtc)`. `SaveMatch` runs in one transaction: insert the `Audiobook` if missing (else bump `UpdatedAtUtc`), replace its `MetadataMatch` row, and set `FileGroup.UserCorrected = 1`. Verify: controller tests.
- [x] **Service.** Add `Services/AudiobookService.cs` with `GetMatch(groupId)` and `PickMatch(groupId, candidate)`, returning a result record like `GroupRenameResult` (match, errors keyed by JSON field name; neither means unknown group). It validates as in Done when, trims text, and serializes the candidate with the web JSON options. Add `BookMatchDto(long GroupId, MetadataCandidateDto Match, DateTimeOffset PickedAtUtc)` to `Models/Api/MetadataDtos.cs`. Register the repository and service as singletons in `Program.cs`. Verify: controller tests.
- [x] **Endpoints.** In `GroupsController.cs` add `GET {id:long}/match` (200 / 204 / 404) and `PUT {id:long}/match` (200 / 400 / 404), both thin over `AudiobookService`. Verify: `GroupMatchControllerTests`.
- Data: two new tables via `CREATE TABLE IF NOT EXISTS` (no migration needed; existing databases gain them at startup). Cascade from `FileGroup` keeps them in step with merges and deletions.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `Api.Tests/Controllers/GroupMatchControllerTests.cs`:
  - a fresh book's `GET match` is 204; `PUT match` with an Audible candidate returns 200 with it, and a following `GET` returns the same candidate and `pickedAtUtc`;
  - picking a second candidate replaces the first (one `MetadataMatch` row) and the group is now `userCorrected`;
  - an unknown group 404s on both; bad candidates (unknown provider, blank title, `ftp://` cover, over-long title) return 400 with the field name and leave no `Audiobook` row;
  - after a pick, a rescan of the unchanged folder keeps the group id and `GET match` still returns the candidate;
  - merging the matched group into another (`POST /api/groups/{other}/files` with all its files) removes its `Audiobook` and `MetadataMatch` rows.
- `Api.Tests/Data/DatabaseInitializerTests.cs`: `Audiobook` and `MetadataMatch` exist with their unique and cascading foreign keys.

## Manual checks
- Run `scripts/start-api.ps1`, scan, take a group id from `GET http://localhost:5070/api/groups`, take a candidate from `GET /api/metadata/search?keywords=...`, then `PUT /api/groups/{id}/match` with it (e.g. `Invoke-RestMethod -Method Put -ContentType application/json -Body $json`). `GET /api/groups/{id}/match` should return it, and the group should show as corrected on the Incoming page.

## Out of scope
- The "Use this match" button and showing the chosen match on the book page (slice 31)
- Copying match values into the book's canonical fields, value origins, the editor (later slice)
- Match scores (no scoring exists yet; add a column when it does)
- Re-fetching the candidate from the provider to verify it
- Clearing a match

## Notes
- File count: 9 (`DatabaseInitializer.cs`, `Models/Local/Audiobook.cs`, `AudiobookRepository.cs`, `AudiobookService.cs`, `MetadataDtos.cs`, `GroupsController.cs`, `Program.cs`, `GroupMatchControllerTests.cs`, `DatabaseInitializerTests.cs`). `Models/Local/Audiobook.cs` is a two-record file, so this is "about 8".
- Split: "picking a match" didn't fit with its UI. This slice is the API; slice 31 adds the book-page button and the chosen-match display.
- Picking a match counts as a user correction (slice 13 decision: any user correction marks the group `UserCorrected`). Without that, the next scan would replace the group and cascade-delete the match, breaking the PRD rule that user choices survive rescans of unchanged files.
- The candidate comes from the client, so it is validated at the boundary (provider, lengths, URL scheme) before it's stored. It isn't re-checked against the provider; the user picked it from a search the API ran moments earlier.
- Excluded and unresolved files have no group, so they can never get an `Audiobook` (carried-in phase 5 rule).
- Conflict check: no new package; logic in a service, thin controller; schema in `DatabaseInitializer`; ids `AUTOINCREMENT` like `FileGroup` (slice 24).
- Remaining phase 5 after 31: the editor with value origins that keeps user edits; manual completion; covers (embedded, download, upload).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 161 passed, 0 failed | Was 155 |
| Frontend tests | 142 passed, 0 failed | Unchanged (no client change) |
| New tests | 6 of 6 present, passing | `GroupMatchControllerTests` (5: fresh 204 then put/get, replace + corrected, 404/400, rescan keeps match, merge cascades), `DatabaseInitializerTests` (1: tables with unique cascading FKs) |
| Manual: pick a live candidate through the API | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |

Data verification queries for the developer (not run):
- `SELECT a.Id, a.GroupId, m.Provider, m.ProviderId, m.PickedAtUtc FROM Audiobook a JOIN MetadataMatch m ON m.AudiobookId = a.Id;`
- `SELECT COUNT(*) FROM MetadataMatch m LEFT JOIN Audiobook a ON a.Id = m.AudiobookId WHERE a.Id IS NULL;` (expect 0)
- `SELECT Id, UserCorrected FROM FileGroup WHERE Id IN (SELECT GroupId FROM Audiobook);` (expect all 1)

## Outcome (2026-10-19, commit b3895e4)

**Status:** Implemented
**Summary:** New `Audiobook` and `MetadataMatch` tables (one per group, cascading from `FileGroup`). `PUT /api/groups/{id}/match` validates and stores the picked candidate as the book's one current match and marks the group `UserCorrected`; `GET /api/groups/{id}/match` returns it, 204 when none, 404 for an unknown group. Matches survive rescans and are removed when their group is merged away.
**Changed:** 9 files: `Data/DatabaseInitializer.cs`, `Data/AudiobookRepository.cs` (new), `Services/AudiobookService.cs` (new), `Models/Local/Audiobook.cs` (new), `Models/Api/MetadataDtos.cs`, `Controllers/GroupsController.cs`, `Program.cs`, and 2 test files.
**Deviations:**
- `GetMatchResult(GroupFound, Match)` lets the controller tell 404 from 204: *fine*.
- Series errors are keyed `series.name` / `series.part`; blank author and narrator names are dropped after trimming: *fine*.
- The `Audiobook` upsert is one `INSERT ... ON CONFLICT ... RETURNING Id`: *improvement*.
- Picking a match sets `UserCorrected` but adds no "Corrected by the user" evidence line: *debt*, minor. Check how slice 25 builds that line when the editor slice next touches the group.
**Follow-ups:**
- Slice 31: "Use this match" on each candidate and the chosen match on the book page (`GET`/`PUT /api/groups/{id}/match`).
- Evidence line for a picked match (debt above).
