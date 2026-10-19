# Slice 25: "Corrected by the user" evidence after file changes

**Phase:** 4, Group corrections
**Goal:** when the user moves files into or out of a book, or excludes or unresolves one of its files, that book's evidence gains the line "Corrected by the user". The rules' original evidence (such as "Same folder") no longer stands alone after the book's files have changed.

## Done when
- [x] Every group whose files change through a move, a merge, a split, an exclude or a send to unresolved (a target, or a source that survives) has "Corrected by the user" appended to its evidence, after the existing lines
- [x] The line is added only once, however many changes follow. It isn't added to groups the user created, which already say "Grouped by the user"
- [x] A rename or a reorder doesn't add it: those don't change which files are in the book, and the row already shows "Edited"
- [x] The evidence survives a rescan, because user-corrected groups are kept
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (Dapper in `Data/`), section 4 (rung 4: let the database do it)
- `docs/slices/17-move-files-and-new-group.md` Outcome: the follow-up this resolves ("group 2 still said 'Same folder' after it gained the stray root file")
- `docs/slices/20-exclude-and-unresolve-api.md` Outcome: `TidyGroups`, shared by moves and `SetUserState`
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`PlaceFiles`, `TidyGroups`, `CreateGroup`), `Services/Grouping/GroupingService.cs` (`ToSummary` reads `EvidenceJson`)
- `src/AudiobookOrganizer.Api.Tests/Services/GroupingServiceTests.cs`

## Tasks
- [x] **Append the line.** In `TidyGroups`, after the totals update, append "Corrected by the user" to `EvidenceJson` for the same surviving group ids. Use SQLite's JSON functions (`json_insert(EvidenceJson, '$[#]', ...)`), skipping groups whose evidence already holds "Corrected by the user" or "Grouped by the user" (`NOT EXISTS (SELECT 1 FROM json_each(EvidenceJson) WHERE value IN (...))`). Keep the text in one constant. Verify: the service tests below.
- [x] **Tests.** Write the tests below. Verify: `dotnet test src/AudiobookOrganizer.Api.Tests` is green with no new build warnings.
- Data: no schema change. `FileGroup.EvidenceJson` gains one line on groups the user changed, in the same transaction as the change.
- Auth: none (no authentication by decision).
- Accessibility: none (API only; the Incoming page already lists evidence lines as text).

## Tests to add
- `Services/GroupingServiceTests.cs`: move one file of "book a" into the single-M4B group. Both groups' evidence ends with "Corrected by the user", after their original lines. A second move between them doesn't add it again.
- `Services/GroupingServiceTests.cs`: excluding a file from "book a" adds the line to "book a". Creating a group from files keeps its evidence as just "Grouped by the user", and moving more files into that group doesn't add the line.
- `Services/GroupingServiceTests.cs`: renaming or reordering a group leaves its evidence unchanged. After a rescan, a moved-into group still ends with "Corrected by the user".

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`). Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan.
  - Move the stray file into the scenario A group: its evidence is "Same folder …", "Filename pattern …", then "Corrected by the user".
  - Rescan: unchanged.
- Delete the scratch folders and database afterwards.

## Out of scope
- Removing or rewriting the rules' evidence lines.
- Any UI change (evidence already shows in the Incoming page's Evidence disclosure).
- Recording what changed or when (no audit history in v1).

## Notes
- **Conflict check:** consistent with the developer's choice (2026-10-18: "Add a 'Corrected by the user' evidence line when files are moved in or out"), PLAN.md phase 4 (reopened), the slice 17 follow-up, and AGENTS.md (logic stays in one repository transaction; SQLite JSON functions are built in, no new package). No `[NEEDS CLARIFICATION]` in scope.
- **Why `TidyGroups`:** it already runs for every group a move, merge, split, exclude or send to unresolved touches, in the same transaction. So one statement covers every path.
- **Size:** 2 tasks, 2 files: `GroupRepository.cs` and `GroupingServiceTests.cs`.
- **Commit:** `feat: slice 25 correction-evidence`.
