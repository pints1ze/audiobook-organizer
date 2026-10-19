# Slice 15: List a group's files and reorder them

**Phase:** 4, Group corrections
**Goal:** `GET /api/groups/{id}` returns a group with its files in order, and `PUT /api/groups/{id}/order` sets that order and marks the group user-corrected. These are the API groundwork for the reorder, move and split UI.

## Done when
- [x] `GET /api/groups/{id}` returns 200 with the group summary fields plus `files`: `[{ fileId, relativePath, position, disc, track, durationMs }]` in position order. `relativePath` is computed as in `GET /api/groups`. An unknown id returns 404
- [x] `PUT /api/groups/{id}/order` with `{ "fileIds": [...] }` rewrites the positions to 1..n in the given order, sets `UserCorrected = 1` and returns 200 with the updated detail. An unknown id returns 404
- [x] The order is rejected with 400 (validation problem keyed `fileIds`), with nothing changed, when the list is missing or empty, has duplicates, or isn't exactly the group's current file set
- [x] A reordered group keeps its order after a rescan of unchanged files
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (thin controllers, logic in `Services/`, Dapper in `Data/`), section 4 (validation at the trust boundary)
- `docs/slices/13-rename-group-keep-corrections.md` Outcome: `UserCorrected`, `GroupRenameResult`, `ReplaceProposed`, and the id reuse follow-up
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`FileGroupFile` has `Position`, `Disc`, `Track`), `Services/Grouping/GroupingService.cs` (`RelativePath`, `IncomingRoot`, `Rename`), `Controllers/GroupsController.cs`, `Models/Api/GroupDtos.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupsControllerTests.cs` and `Services/GroupingServiceTests.cs` (the `StoredGroups` and positions helpers)

## Tasks
- [x] **Detail read.** In `GroupRepository`, add `GetGroupFiles(groupId)`: join `FileGroupFile` to `AudioFile` and return the file id, source path, position, disc, track and duration, ordered by `Position`. In `GroupDtos.cs`, add `GroupFileDto(long FileId, string RelativePath, int Position, int? Disc, int? Track, long? DurationMs)` and `GroupDetailDto`, which has the summary fields plus `Files`. Add `GroupingService.GetDetail(id)` (null when unknown) and `[HttpGet("{id:long}")]`. Verify: the controller test below.
- [x] **Reorder.** In `GroupRepository`, add `SetOrder(groupId, fileIds)`. In one transaction it updates each `FileGroupFile.Position` to its 1-based index and sets `UserCorrected = 1`. Add `GroupingService.Reorder(id, request)`. It loads the group's current file ids, validates the request against them (non-empty, no duplicates, same set) and returns a result shaped like `GroupRenameResult` with the detail, or errors keyed `fileIds`, or neither for an unknown id. Add `ReorderGroupDto(IReadOnlyList<long>? FileIds)` and `[HttpPut("{id:long}/order")]`, mapping 200, 400 and 404 the way `Put` does. Verify: the tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data: none (uses the existing `FileGroupFile.Position` and `FileGroup.UserCorrected`).
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the UI comes in a later slice).

## Tests to add
- `Controllers/GroupsControllerTests.cs`: after a scan with a two-file folder, `GET /api/groups/{id}` returns both files with relative paths and positions 1 and 2 in order, and an unknown id returns 404.
- `Controllers/GroupsControllerTests.cs`: `PUT /api/groups/{id}/order` with the two ids reversed returns 200 with the files in the new order and `userCorrected: true`, and a following `GET` shows the same.
- `Controllers/GroupsControllerTests.cs`: each of these returns 400 with `errors.fileIds` and leaves the detail unchanged: an empty list, a duplicated id, a missing id, and an id from another group. An unknown group returns 404.
- `Services/GroupingServiceTests.cs`: reorder the "book a" group, then rescan with no file changes. The stored positions are still the reordered ones.

## Manual checks
- Run the API against a scratch database (`Database__Path`), not the dev `data/audiobooks.db`. Lay out a scenario A folder with three tracks (copies of the fixtures) and scan.
  - `GET /api/groups/{id}` lists the three files in track order.
  - `PUT .../order` with the order reversed returns them reversed.
  - Rescan, then `GET` again: the order is unchanged.
- Delete the scratch folders and database afterwards.

## Out of scope
- Any UI for the file list or reordering (next slice).
- Moving files between groups, splitting, merging and excluding (later phase 4 slices). They'll reuse `GetGroupFiles` and the detail DTO.
- Changing the stored `Disc` and `Track` values. Reordering changes only `Position`.
- Stable group ids across rescans (open question from slice 13).

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 ("reorder ... work from the UI; corrections survive a rescan"), the slice 13 decision that any correction marks the whole group `UserCorrected` (so a rescan already leaves its `FileGroupFile` rows alone), and AGENTS.md (no new packages, logic in `Services/`). No `[NEEDS CLARIFICATION]` in scope.
- **Ordering:** this slice puts the reorder and detail endpoints first because the move, split and reorder UI all need a group's file list. The UI for the file list and reordering is the next slice (`types/groups.ts`, `services/api.ts` and its test, `IncomingPage.tsx` and its test).
- **Size:** 3 tasks, 6 files: `GroupRepository.cs`, `GroupingService.cs` and its test, `GroupsController.cs` and its test, `GroupDtos.cs`.
- **Commit:** `feat: slice 15 group-files-and-reorder`.
