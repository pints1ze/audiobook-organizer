# Slice 17: Move files between groups and create a group from files

**Phase:** 4, Group corrections
**Goal:** two API operations cover move file, merge and split. `POST /api/groups/{id}/files` moves files (from other groups or from unresolved) into a group. `POST /api/groups` creates a new user-made group from chosen files. Every touched group is marked user-corrected and survives rescans.

## Done when
- [x] `POST /api/groups/{id}/files` with `{ "fileIds": [...] }` moves those files into group `id`, appended after its last position in the given order, and returns 200 with the target's detail (the slice 15 shape). Each file leaves its old group; the old group's remaining files are renumbered 1..n; an old group left with no files is deleted. The target and every source group that still exists get `UserCorrected = 1`, and their `FileCount` and `TotalDurationMs` are recomputed from their files. An unknown target returns 404
- [x] `POST /api/groups` with `{ "fileIds": [...], "title": "...", "author": "..." }` creates a group from those files in the given order (taking them out of their old groups the same way). It has `UserCorrected = 1`, `Confidence = "High"`, evidence `["Grouped by the user"]`, title and author cleaned and checked like `PUT /api/groups/{id}`, and recomputed totals. It returns 201 with the detail and a `Location` of `/api/groups/{newId}`
- [x] Both return 400 (validation problem keyed `fileIds`, or `title`/`author` for create) with nothing changed when the list is missing or empty or has duplicates, when an id isn't a file from the latest completed scan that was read without error, or (for move) when a file is already in the target
- [x] Merging works as moving every file of group B into group A: B is deleted. Moving an unresolved file into a group removes it from `unresolved`
- [x] After a rescan of unchanged files, the moved and created groups keep their files, order, titles and totals
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, Dapper in `Data/`), section 4 (validation at the trust boundary, error handling that prevents data loss)
- Outcomes of `docs/slices/13-rename-group-keep-corrections.md` (`UserCorrected`, `GroupRenameResult`, the id reuse follow-up) and `docs/slices/15-group-files-and-reorder.md` (`GetGroupFiles`, `GroupDetailDto`, `GroupReorderResult`, the `Invalid` helper)
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`GetUngroupedFiles` shows the "latest completed scan, no error" filter), `Services/Grouping/GroupingService.cs`, `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`, `Data/ScanRepository.cs` (`GetLatestCompletedRun`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupsControllerTests.cs` and `Services/GroupingServiceTests.cs`

## Tasks
- [x] **Repository.**
  - Add `GetMovableFileIds(scanId, fileIds)`: the subset of `fileIds` that are AudioFile rows with `LastSeenScanId = scanId` and `ScanError IS NULL`.
  - Add `MoveFiles(targetGroupId, fileIds)` and `CreateGroup(title, author, fileIds, scanRunId)`. Each runs in one transaction. They delete the files' existing `FileGroupFile` rows and insert them into the target (after `MAX(Position)`) or the new group in order. Then they renumber each source group's remaining positions 1..n, delete source groups left with no files, and recompute `FileCount` and `TotalDurationMs` (sum of `DurationMs`, nulls as 0) for the target and the surviving sources, all marked `UserCorrected = 1`.
  - `CreateGroup` returns the new id.

  Verify: the service tests below.
- [x] **Service and DTOs.** Add `MoveFilesDto(IReadOnlyList<long>? FileIds)` and `CreateGroupDto(IReadOnlyList<long>? FileIds, string? Title, string? Author)`. Add `GroupingService.MoveFiles(id, request)` and `CreateGroup(request)`, returning results shaped like `GroupReorderResult`.
  - Validate in this order: unknown target (404), then an empty list or duplicates, then ids not movable from the latest completed scan, then (for move) ids already in the target.
  - Reuse the title/author cleaning and the 500-character check from `Rename`. Pull it into a private helper; don't copy it.
  - Return the detail through `GetDetail`.

  Verify: the tests below.
- [x] **Endpoints.** Add `[HttpPost("{id:long}/files")]` (200, 400 or 404) and `[HttpPost]` (201 via `CreatedAtAction` to the `GET {id}` action, or 400) in `GroupsController`, reusing `Invalid`. Verify: the controller tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data: no schema change. Operations rewrite `FileGroupFile` rows and `FileGroup` totals, and delete emptied groups. Each operation is one transaction, so a failure leaves the groups as they were. No audio file is touched.
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the UI comes in a later slice).

## Tests to add
- `Services/GroupingServiceTests.cs`: move one file from the two-file "book a" group into the single-M4B group. The target has 2 files at positions 1–2 with the summed duration and `UserCorrected`. "book a" has 1 file at position 1 with its totals recomputed and `UserCorrected`. A rescan with no changes keeps all of that.
- `Services/GroupingServiceTests.cs`: merging (moving both "book a" files into the M4B group) deletes "book a". Creating a group from one "book a" file and the M4B file gives a new group with the given title, both files in the given order, `Confidence` "High" and evidence "Grouped by the user"; the M4B's old group is deleted.
- `Controllers/GroupsControllerTests.cs`: after a scan with two unresolved root MP3s, `POST /api/groups` with both ids returns 201 with a `Location` and the detail, and `GET /api/groups` then shows no unresolved files. `POST /api/groups/{id}/files` with an unresolved file moves it in and returns 200.
- `Controllers/GroupsControllerTests.cs`: each of these returns 400 and leaves `GET /api/groups` unchanged: an empty list, a duplicate, an unknown file id, a file already in the target, and a 501-character title on create. An unknown target returns 404.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`). Lay out scenario A (three tracks in a folder), one M4B and one stray root MP3 (copies of the fixtures), and scan.
  - Move the stray MP3 into the scenario A group: it's appended, and `unresolved` is empty.
  - Create a new group from two of the scenario A tracks with a title: 201, and the old group keeps the remaining tracks, renumbered.
  - Merge the M4B group into the new group: the M4B group is gone.
  - Rescan: everything is unchanged, and the totals match the files.
- Delete the scratch folders and database afterwards.

## Out of scope
- UI for move, merge and split (next slice).
- Excluding files, or moving a file back to unresolved. Both depend on the exclude decision (open question, raised here).
- Stable group ids across rescans (open question from slice 13). New groups use the next rowid as before.
- Changing disc or track values.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("merge, split, move file ... work from the UI; corrections survive a rescan"), the slice 13 decision that a corrected group's membership is frozen (moved files sit in corrected groups, so rescans skip them), AGENTS.md (no new packages, logic in `Services/`, transactions so a failure loses nothing) and the PRD (scans never touch files; this changes only database rows). No `[NEEDS CLARIFICATION]` in scope.
- **Assumption:** a user-made group gets `Confidence = "High"` because `FileGroup.Confidence` is `NOT NULL`, and the user's grouping is the strongest evidence there is. Its evidence says so in words.
- **One primitive:** merge is a move of all of B's files into A, and split is a create from the chosen files. Separate merge or split endpoints would duplicate the same transaction (AGENTS.md section 4).
- **Size:** 4 tasks, 6 files: `GroupRepository.cs`, `GroupingService.cs` and its test, `GroupsController.cs` and its test, `GroupDtos.cs`.
- **Commit:** `feat: slice 17 move-files-and-new-group`.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 100 passed, 0 failed | 94 before plus 6 new |
| Frontend tests | 81 passed, 0 failed | no frontend change in this slice |
| New tests | 4 of 4 behaviours present, passing | `Moving_a_file_appends_it_renumbers_the_source_recomputes_totals_and_survives_a_rescan`, `Merging_deletes_the_emptied_group`, `Creating_a_group_takes_files_in_order_and_deletes_emptied_groups`, `Post_creates_a_group_from_unresolved_files_and_returns_201_with_a_location`, `Post_files_moves_an_unresolved_file_into_a_group`, `Bad_move_and_create_requests_return_400_without_changing_anything_and_an_unknown_target_404s` |
| Manual: move the stray file in | Passed | API on :5070 with a scratch DB (`Database__Path`). Scenario A (3 tracks), one M4B and a stray root MP3; the scan gave 2 groups and 1 unresolved. `POST /api/groups/2/files` `[2]` returned 200 with the file at position 4, 4 files, 120000 ms and `userCorrected: true`. `unresolved` was then empty |
| Manual: create (split) | Passed | `POST /api/groups` with files 3 and 4, title "Split Book": 201 with `Location: /api/groups/3`, High, "Grouped by the user", 2 files, 60000 ms. Group 2 kept `03 track.mp3` and `stray thing.mp3`, renumbered 1–2, with 60000 ms |
| Manual: merge | Passed | Moving the M4B group's file into group 3 returned 200; `GET /api/groups/1` then returned 404 |
| Manual: rescan | Passed | Completed with 5 unchanged. `GET /api/groups` was byte-identical before and after; the files, order and totals match |

**Data:** a scratch DB and folders in the session scratchpad, not the dev `data/audiobooks.db`. The server and all scratch data were removed afterwards.

## Outcome (2026-10-18, commit e1569b4)

**Status:** Implemented
**Summary:** Two endpoints give move file, merge and split:
- `POST /api/groups/{id}/files` moves files (from groups or unresolved) into a group.
- `POST /api/groups` creates a user-made group (High, "Grouped by the user") from chosen files.

Both share one transactional `PlaceFiles` helper. It renumbers the source groups, deletes emptied ones, recomputes totals and marks every touched group user-corrected. Bad lists or titles return 400 with nothing changed. The groups survived a rescan byte for byte. All Done when items are met.
**Changed:** 6 code files plus 3 docs. Key ones: `Data/GroupRepository.cs`, `Services/Grouping/GroupingService.cs`, `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`.
**Deviations:**
- A moved file keeps the disc and track from its old group; an unresolved file takes them from its tags. *Fine:* the slice didn't say.
- On create, file-list and title/author errors come back together in one 400. *Improvement.*
- New `GroupFilesResult` record. *Fine.*
- Title/author cleaning moved into a shared `CleanTitleAuthor` helper used by `Rename`. *Fine*, as the slice asked.
**Follow-ups:**
- Next slice: the move, merge and split UI on the Incoming page.
- A group's evidence isn't updated when files are moved in or out. In the manual run, group 2 still said "Same folder" after it gained the stray root file. *Minor debt:* consider an extra evidence line such as "Corrected by the user" when the UI shows it.
