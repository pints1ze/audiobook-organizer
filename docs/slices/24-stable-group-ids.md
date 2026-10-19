# Slice 24: Stable group ids

**Phase:** 4, Group corrections
**Goal:** a group id is never reused. `FileGroup.Id` becomes `INTEGER PRIMARY KEY AUTOINCREMENT`, so a page holding an old id gets a 404 instead of editing a different book. Existing databases are rebuilt once, keeping every row.

## Done when
- [x] New databases create `FileGroup` with `Id INTEGER PRIMARY KEY AUTOINCREMENT`
- [x] On startup, a database whose `FileGroup` was created without `AUTOINCREMENT` (detected from its `sqlite_master` SQL) is rebuilt in one transaction. Every group row and id is kept, every `FileGroupFile` row still points to its group, and `PRAGMA foreign_key_check` reports nothing. A second start doesn't rebuild again
- [x] After a group with the highest id is deleted (by a rescan replacing it, or by a merge emptying it), the next new group gets a higher id than any id used before
- [x] All four checks pass

## Read first
- `AGENTS.md`: section 4 (error handling that prevents data loss), Don't touch (`*.db`: tests use temp databases only)
- `docs/CURRENT-STATE.md` Decisions: slice 05 (no migration framework), slice 11 (foreign keys enforced on every connection), slice 13 (guarded `ALTER`), and the developer's decision that ids stay stable
- `docs/slices/13-rename-group-keep-corrections.md` Outcome: the id-reuse debt this resolves
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs`, `Data/SqliteConnectionFactory.cs` (it turns `foreign_keys` on for every connection)
- `src/AudiobookOrganizer.Api.Tests/Data/DatabaseInitializerTests.cs` (the old-schema tests build tables by hand), `Services/GroupingServiceTests.cs`

## Tasks
- [x] **Schema.** In `DatabaseInitializer`, add `AUTOINCREMENT` to `FileGroup`'s `CREATE TABLE IF NOT EXISTS`.
- [x] **One-time rebuild.** After the `UserCorrected` guard, check `SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'FileGroup'` for `AUTOINCREMENT`. When it's missing, rebuild using SQLite's documented table-change procedure:
  1. Set `PRAGMA foreign_keys = OFF` (outside the transaction).
  2. `BEGIN`, then create `FileGroup_new` with the new definition.
  3. `INSERT INTO FileGroup_new (...) SELECT ... FROM FileGroup`, listing the columns.
  4. `DROP TABLE FileGroup`, then `ALTER TABLE FileGroup_new RENAME TO FileGroup`.
  5. Run `PRAGMA foreign_key_check` and throw (rolling back) if it returns any row; otherwise `COMMIT`.
  6. Set `PRAGMA foreign_keys = ON` again in a `finally`.

  Put it in a private method with a comment that says why. Verify: the schema tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data:
  - `FileGroup` gains `AUTOINCREMENT`, through a one-time rebuild of existing databases in a single transaction that keeps every row and id.
  - A failure rolls back and leaves the old table in place, and the app reports the error at startup like any other initialiser error.
  - No audio file is touched.
- Auth: none (no authentication by decision).
- Accessibility: none (no UI change).

## Tests to add
- `Data/DatabaseInitializerTests.cs`: start from an old database with `FileGroup` built by hand without `AUTOINCREMENT`, holding two groups (ids 1 and 5), a `FileGroupFile` row for each, and the `AudioFile` and `ScanRun` rows they need. After initialising:
  - `sqlite_master` shows `AUTOINCREMENT`;
  - both groups keep their ids and columns;
  - both `FileGroupFile` rows remain;
  - `foreign_key_check` is empty.
  Initialising a second time changes nothing.
- `Data/DatabaseInitializerTests.cs`: after the rebuild, delete group 5 and insert a new group. Its id is greater than 5.
- `Services/GroupingServiceTests.cs`: rescan after a change that replaces the highest-id uncorrected group. The new group's id is higher than every id before the rescan, so the old id now returns null from `GetDetail`.

## Manual checks
- Build commit 9482e6d in a temporary worktree and run it on a scratch database (`Database__Path`, not the dev `data/audiobooks.db`). Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), scan, rename one group and note `GET /api/groups`.
- Start the new API on that database. `GET /api/groups` is byte-identical, and the `FileGroup` SQL (read through a second start's behaviour, not by running SQL by hand) has been rebuilt: merging the highest-id group into another, then creating a group, gives a new, higher id. Restart once more: it starts cleanly.
- Remove the worktree, and delete the scratch folders and databases afterwards.

## Out of scope
- `AUTOINCREMENT` on other tables (`ScanRun` and `AudioFile` ids are never deleted and reused in practice).
- Stable ids for uncorrected groups across rescans (they're still replaced each scan, but with new ids, never old ones).
- Any client change: a stale id already produces a 404, which the Incoming page handles by reloading.

## Notes
- **Conflict check:** consistent with the developer's decision (2026-10-18) that ids stay stable (never reused), PLAN.md phase 4 (reopened), the slice 05 decision (no migration framework; idempotent schema code in `DatabaseInitializer`), slice 11 (foreign keys on, so they must be turned off for the rebuild) and AGENTS.md (no data loss: one transaction plus `foreign_key_check`). No `[NEEDS CLARIFICATION]` in scope.
- **Why a rebuild:** SQLite can't add `AUTOINCREMENT` with `ALTER TABLE`. The documented procedure is: create the new table, copy the rows, drop the old table, rename the new one. `FileGroupFile`'s foreign key refers to `FileGroup` by name, so after the rename it points to the new table.
- **Size:** 3 tasks, 3 files: `DatabaseInitializer.cs` and its test, `GroupingServiceTests.cs`.
- **Commit:** `feat: slice 24 stable-group-ids`.
