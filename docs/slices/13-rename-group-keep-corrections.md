# Slice 13: Rename a group and keep corrections across rescans

**Phase:** 4, Group corrections
**Goal:** `PUT /api/groups/{id}` sets a group's title and author and marks it user-corrected, and a later scan keeps every user-corrected group (and its files) instead of replacing it.

## Done when
- [x] `FileGroup` has a `UserCorrected` flag (default 0). A database created before this slice gets the column on startup, with no data loss
- [x] `PUT /api/groups/{id}` with `{ "title": "...", "author": "..." }` trims both, stores blank as null, sets `UserCorrected = 1` and returns 200 with the updated group. An unknown id returns 404; a title or author over 500 characters returns 400 with a validation problem
- [x] `GET /api/groups` includes `userCorrected` on each group
- [x] After a completed rescan, a user-corrected group keeps its id, title, author and files. Its files aren't regrouped into any other group, and they don't show as unresolved. Groups not corrected are still replaced as before
- [x] The `// shortcut:` comment in `GroupingService.Regroup` is gone
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, schema in `DatabaseInitializer`), section 4 (validation at the trust boundary)
- `docs/CURRENT-STATE.md` decisions for slices 05 (`CREATE TABLE IF NOT EXISTS`, no migration framework), 07 (AudioFile rows are upserted on `SourcePath`, so ids are stable across rescans; missing files keep their rows) and 11 (groups fully replaced, a shortcut until phase 4)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs`, `Data/GroupRepository.cs`, `Models/Local/FileGroup.cs`
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingService.cs`, `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`
- `src/AudiobookOrganizer.Api/Controllers/SettingsController.cs` and `Services/SettingsService.cs` for how validation errors are returned today; match that style
- Tests: `Services/GroupingServiceTests.cs`, `Controllers/GroupsControllerTests.cs`, `Data/DatabaseInitializerTests.cs`

## Tasks
- [x] **Schema.** In `DatabaseInitializer`, add `UserCorrected INTEGER NOT NULL DEFAULT 0` to the `FileGroup` create statement. After the create, check `PRAGMA table_info(FileGroup)` and run `ALTER TABLE FileGroup ADD COLUMN UserCorrected INTEGER NOT NULL DEFAULT 0` when it's missing (an existing dev or container database). Add `bool UserCorrected` to `Models/Local/FileGroup.cs` and to the `GetGroups` select. Verify: the initializer test below.
- [x] **Regroup keeps corrected groups.** In `GroupRepository`:
  - `GetGroupableFiles` also excludes files that belong to a group with `UserCorrected = 1`.
  - `ReplaceAll` becomes `ReplaceProposed`: it deletes only groups with `UserCorrected = 0` (the cascade still removes their `FileGroupFile` rows) and inserts the new ones, in one transaction.

  Update `GroupingService.Regroup` to call it and remove the shortcut comment. Corrected groups are not touched: their file count and duration stay as stored. Verify: the service tests below.
- [x] **Rename.** Add `GroupRepository.UpdateTitleAuthor(id, title, author)` that sets both and `UserCorrected = 1` and returns whether a row changed. Add `GroupingService.Rename(id, request)` that trims, turns blank into null, checks the 500-character limit and returns the updated `GroupSummaryDto` (or null for an unknown id). Add `UpdateGroupDto(string? Title, string? Author)` in `GroupDtos.cs`, `UserCorrected` to `GroupSummaryDto`, and `[HttpPut("{id:long}")]` in `GroupsController`: 400 with a validation problem for a too-long value, 404 for an unknown id, 200 with the group otherwise. Verify: the controller tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green, and the build has no new warnings.
- Data: one new column, `FileGroup.UserCorrected`, added by `CREATE TABLE` for new databases and by a guarded `ALTER TABLE ... ADD COLUMN` for existing ones. Existing groups default to not corrected.
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the UI comes in slice 14).

## Tests to add
- `Data/DatabaseInitializerTests.cs`: a database created with the old `FileGroup` schema (no `UserCorrected`) holding one row gets the column on `Initialize()`, the row survives with `UserCorrected = 0`, and a second `Initialize()` doesn't fail.
- `Services/GroupingServiceTests.cs`: scan, rename the "book a" group, rescan with no file changes. The group keeps its id, title, author, `UserCorrected` and both files, and no other group contains those files.
- `Services/GroupingServiceTests.cs`: after a rename, a new file added to incoming is still grouped by the engine on rescan, and the uncorrected groups are replaced (new ids) while the corrected one is not.
- `Controllers/GroupsControllerTests.cs`: after a scan, `PUT /api/groups/{id}` with `"  New Title  "` and `"   "` returns 200 with title `"New Title"`, author null and `userCorrected: true`, and `GET /api/groups` shows the same.
- `Controllers/GroupsControllerTests.cs`: `PUT` on an unknown id returns 404, and a 501-character title returns 400 and changes nothing.

## Manual checks
- Run the API, lay out a temp incoming folder like scenario A (copies of the fixtures), scan, then `PUT /api/groups/{id}` with a new title. Rescan from the Dashboard and check `GET /api/groups`: the group keeps the new title and its id, and the file isn't listed as unresolved.
- Start the API once against a dev database created before this slice (or one copied from the last phase's verification): it starts, and the existing groups come back with `userCorrected: false`.
- Delete the temp folders and the dev database afterwards.

## Out of scope
- Any UI for renaming or for showing the corrected state (slice 14).
- Merge, split, move file, exclude and reorder (later phase 4 slices). Each will also set `UserCorrected`.
- Recounting a corrected group's file count or duration when its files change or go missing on disk. The group stays as stored; handling missing members comes with move/exclude.
- Dashboard group and status counts, and moving the dashboard query into a service (carried into phase 4; a later slice).
- Grouping-rule tweaks from the developer's real-collection check (a later slice, once the developer lists them).

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("set title/author", "corrections survive a rescan of unchanged files"), PRD workflow 3 and the FileGroup "user-corrected flag", the slice 05 no-migration decision (a guarded `ALTER` keeps it that way) and the slice 11 shortcut it retires. AudioFile ids are stable across rescans (upsert on `SourcePath`), so `FileGroupFile` links in corrected groups stay valid. No new dependencies.
- **Assumption:** a renamed group counts as "corrected" as a whole, so its membership is frozen too. That matches how the merge/split/move slices will need it.
- **Split:** the full "set title/author from the UI" piece was about 14 files. This slice is the API and persistence; slice 14 is the Incoming page edit form (`types/groups.ts`, `services/api.ts` and its test, `IncomingPage.tsx` and its test).
- **Size:** 4 tasks, about 9 files: `DatabaseInitializer.cs` and its test, `GroupRepository.cs`, `FileGroup.cs`, `GroupingService.cs` and its test, `GroupsController.cs` and its test, `GroupDtos.cs`.
- **Commit:** `feat: slice 13 rename-group-keep-corrections`.
