# Slice 20: Exclude files and send them back to unresolved (API)

**Phase:** 4, Group corrections
**Goal:** two API operations let the user take files out of their books. `POST /api/groups/excluded` excludes them, and `POST /api/groups/unresolved` sends them back to unresolved. Undoing an exclude is the same as sending the file to unresolved. The state is kept on the file, so it survives rescans.

## Done when
- [x] `AudioFile` has a nullable `UserState` column (`'Excluded'`, `'Unresolved'` or null). It's created for new databases and added to existing ones by a guarded `ALTER TABLE ... ADD COLUMN`, like `FileGroup.UserCorrected`
- [x] `POST /api/groups/excluded` with `{ "fileIds": [...] }` takes the files out of their groups and sets `UserState = 'Excluded'`. Source groups are handled as moves are: the remaining files are renumbered 1..n, emptied groups are deleted, and totals are recomputed with the groups marked user-corrected. It returns 200 with the new `GET /api/groups` list
- [x] `POST /api/groups/unresolved` does the same with `UserState = 'Unresolved'`, including for currently excluded files (that's the undo)
- [x] `GET /api/groups` gains `excluded: [{ fileId, relativePath, durationMs }]` (ordered by path). `unresolved` no longer lists excluded files
- [x] After a rescan of unchanged files, excluded files stay excluded and unresolved files stay unresolved. The grouping engine never sees a file with a `UserState`
- [x] Moving a file into a group or creating a group from it (`POST /api/groups/{id}/files`, `POST /api/groups`) clears its `UserState`
- [x] Both new endpoints return 400 (keyed `fileIds`) with nothing changed for an empty list, duplicates, or ids that aren't error-free files from the latest completed scan. They also return 400 when a file is already in the requested state: already excluded, or already unresolved and not excluded
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, Dapper in `Data/`), section 4 (validation at the trust boundary; transactions so a failure loses nothing)
- `docs/CURRENT-STATE.md` Decisions: slice 13 (`UserCorrected`, guarded `ALTER`), slice 17 (merge and split as moves), slice 18 (the developer's exclude decision)
- `docs/slices/17-move-files-and-new-group.md` Outcome: `PlaceFiles`, `FileIdsError`, `GroupFilesResult`
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs`, `Data/GroupRepository.cs` (`GetGroupableFiles`, `GetUngroupedFiles`, `PlaceFiles`), `Data/ScanRepository.cs` (`UpsertFile` keeps columns it doesn't list), `Services/Grouping/GroupingService.cs`, `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupsControllerTests.cs`, `Services/GroupingServiceTests.cs`, `Data/DatabaseInitializerTests.cs`

## Tasks
- [x] **Schema.** In `DatabaseInitializer`, add `UserState TEXT` to `AudioFile`, plus a guarded `ALTER TABLE AudioFile ADD COLUMN UserState TEXT` for older databases. Verify: a `DatabaseInitializerTests` case that creates the old `AudioFile` table without the column, initialises it, and finds the column there.
- [x] **Repository.**
  - `GetGroupableFiles`: add `AND a.UserState IS NULL`.
  - `GetUngroupedFiles`: add `AND (a.UserState IS NULL OR a.UserState <> 'Excluded')`.
  - Add `GetExcludedFiles(scanId)`: files from that scan with `UserState = 'Excluded'`, ordered by `SourcePath`.
  - Add `SetUserState(fileIds, state)`: one transaction that deletes the files' `FileGroupFile` rows, tidies the source groups (renumber, delete when empty, recompute totals, mark corrected) and sets `UserState`.
  - Split the source-tidying half of `PlaceFiles` into a private helper that both use. `PlaceFiles` also sets `UserState = NULL` on the files it places.
  - Add `GetUserStates(fileIds)` for validation.

  Verify: the service tests below.
- [x] **Service, DTO and endpoints.**
  - Add `Excluded` to `GroupListDto`, filled by `List()`.
  - Add `GroupingService.Exclude(request)` and `Unresolve(request)`, taking `MoveFilesDto`. They validate with `FileIdsError`, then reject files already in the requested state, then call `SetUserState`, and return a result carrying `List()` or errors.
  - Add `[HttpPost("excluded")]` and `[HttpPost("unresolved")]` to `GroupsController`, returning 200 with the list or `Invalid`.

  Verify: the controller tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data:
  - New nullable column `AudioFile.UserState`, added by guarded `ALTER`. Existing rows get null, which means "the rules decide".
  - Each operation is one transaction. No audio file is touched.
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the UI is the next slice).

## Tests to add
- `Services/GroupingServiceTests.cs`: exclude one file of the two-file "book a" group. "book a" has 1 file at position 1 with recomputed totals and `UserCorrected`. `List()` shows the file under `Excluded` and not `Unresolved`. A rescan with no changes keeps all of that.
- `Services/GroupingServiceTests.cs`: send a file of an uncorrected group to unresolved, then rescan. It's still unresolved, not regrouped. Then move it into a group: its `UserState` is cleared, so it's in the group after another rescan.
- `Services/GroupingServiceTests.cs`: excluding the only file of the single-M4B group deletes that group. Sending the excluded file to unresolved moves it from `Excluded` to `Unresolved` (the undo).
- `Controllers/GroupsControllerTests.cs`: `POST /api/groups/excluded` returns 200 with `excluded` holding the file. Each of these returns 400 keyed `fileIds` and leaves `GET /api/groups` unchanged: an empty list, a duplicate, an unknown id, and an already-excluded file. `POST /api/groups/unresolved` with a file that's already unresolved returns 400.
- `Data/DatabaseInitializerTests.cs`: an `AudioFile` table without `UserState` gains the column on initialise, and the existing rows are kept.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`). Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan.
  - Exclude one scenario A track: the group has 2 files renumbered 1–2, and `excluded` lists the track.
  - Send another scenario A track to unresolved: it's listed under `unresolved`.
  - Rescan: the excluded file is still excluded, the unresolved one is still unresolved, and the groups are byte-identical.
  - Send the excluded track to unresolved (undo), then move it into the scenario A group. Rescan: it stays in the group.
- Start the new API on a database created by the slice 19 build (commit 5b8e382, built in a temporary worktree). The column is added, and `GET /api/groups` works. Remove the worktree afterwards.
- Delete the scratch folders and database afterwards.

## Out of scope
- UI for exclude, the Excluded list and undo (next slice).
- Excluded files in later phases (metadata, processing). They'll read `UserState` when those phases start.
- Evidence updates after manual changes (slice 17 follow-up).
- Stable group ids (open question).

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("exclude ... corrections survive a rescan"). It follows the developer's decision (slice 18): excluded until undone, survives rescans, files can be sent back to unresolved, and unresolved files stay unresolved across rescans. It matches the slice 13 guarded `ALTER` pattern, the slice 17 move semantics and AGENTS.md (no new packages, transactions). No `[NEEDS CLARIFICATION]` in scope.
- **Why a state on the file:** both "excluded" and "kept unresolved" must outlive a rescan, while the file isn't in any group. A column on `AudioFile` survives because `UpsertFile` only updates the columns it lists, and it keeps one row per file.
- **Undo:** undoing an exclude is sending the file to unresolved, so there's no third endpoint. From there the user moves it into a book. A file only returns to the rules' grouping after it has been placed in a group, which clears the state; that's a deliberate, simple rule.
- **Assumption:** a file that changes on disk keeps its `UserState` (the upsert doesn't touch it).
- **Size:** 4 tasks, 8 files: `DatabaseInitializer.cs` and its test, `GroupRepository.cs`, `GroupingService.cs` and its test, `GroupsController.cs` and its test, `GroupDtos.cs`.
- **Commit:** `feat: slice 20 exclude-and-unresolve-api`.
