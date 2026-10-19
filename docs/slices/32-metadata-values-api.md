# Slice 32: Metadata values API

**Phase:** 5, Metadata
**Goal:** each book has stored metadata values, each with its origin. Picking a match fills in provider values, and values the user entered survive later picks.

## Done when
- [x] `GET /api/groups/{id}/metadata` returns `{ groupId, fields }` with one entry per field: `title`, `subtitle`, `authors`, `narrators`, `series`, `seriesPart`, `publisher`, `releaseDate`, `language`, `format`, `asin`, `isbn`. Each entry is `{ value, origin, provider }`. `origin` is `"file"`, `"provider"`, `"user"` or null, and `provider` is set only when origin is `"provider"`. `authors` and `narrators` are string arrays (`[]` when empty). The rest are strings or null. It returns 404 for an unknown group.
- [x] A book with no stored values falls back to the group's title (`title`) and author (`authors: [author]`) with origin `"file"`. Every other field is null with origin null.
- [x] `PUT /api/groups/{id}/metadata` with `{ fields: { <name>: value, ... } }` stores only the fields it sends, each with origin `"user"`. A blank string or empty array is stored as an explicit empty user value. It creates the book's `Audiobook` if missing, marks the group `UserCorrected`, and returns the full metadata. Blank `title` → 400 "The title is required."; over-long text or too many names → 400 with the same messages as the match endpoint; unknown group → 404.
- [x] `PUT /api/groups/{id}/match` also copies the candidate into the values, following these rules: (a) a field with origin `"user"` is never changed; (b) a candidate with no value for a field (null, blank or empty list) leaves that field as it is; (c) an `openlibrary` candidate never replaces a value whose provider is `audible`. Copied values get origin `"provider"` and the candidate's provider. The candidate's `series.name` and `series.part` map to `series` and `seriesPart`.
- [x] All four checks pass.

## Read first
- `docs/slices/30-pick-match-api.md`, `## Outcome`
- `docs/PRD.md`, Data and Rules (origins; user values survive searches; provider gaps keep existing values; print never replaces audiobook data)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`PickMatch`, `Clean`, error messages)
- `src/AudiobookOrganizer.Api/Data/AudiobookRepository.cs`, `Data/DatabaseInitializer.cs` (the `Audiobook` and `MetadataMatch` tables)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs`, `Models/Local/Audiobook.cs`
- `src/AudiobookOrganizer.Api/Controllers/GroupsController.cs` (`GetMatch`, `PutMatch`, `Invalid`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMatchControllerTests.cs` (scan helpers, `AudibleCandidate`)

## Tasks
- [x] **Schema and repository.** In `DatabaseInitializer.cs` add `AudiobookField (AudiobookId INTEGER NOT NULL REFERENCES Audiobook (Id) ON DELETE CASCADE, Field TEXT NOT NULL, ValueJson TEXT NOT NULL, Origin TEXT NOT NULL, Provider TEXT, PRIMARY KEY (AudiobookId, Field))`. Add an `AudiobookField` entity in `Models/Local/Audiobook.cs`. In `AudiobookRepository.cs` add `GetFields(groupId)` and `SaveFields(groupId, fields, nowUtc)`, which upserts the given rows, creates the `Audiobook` if missing and sets `UserCorrected = 1` in one transaction. Make `SaveMatch` take the rows to upsert too, so the match and its values are saved in the same transaction. Verify: the tests below, and the `DatabaseInitializerTests` still pass.
- [x] **Read and edit.** In `MetadataDtos.cs` add `MetadataFieldDto<T>(T Value, string? Origin, string? Provider)`, `BookMetadataDto(long GroupId, BookMetadataFieldsDto Fields)` (one property per field, list fields typed `IReadOnlyList<string>`) and `UpdateBookMetadataDto` (one nullable property per field, so only the fields sent are set). In `AudiobookService.cs` add `GetMetadata(groupId)` (stored rows, then the group fallback) and `UpdateMetadata(groupId, request)`, reusing the trimming and limits from `Clean`. In `GroupsController.cs` add `GET` and `PUT {id:long}/metadata`. Verify: `GroupMetadataControllerTests`.
- [x] **Picking applies values.** In `PickMatch`, after cleaning, build the rows to copy from the candidate under rules (a)–(c) against the current rows, and pass them to `SaveMatch`. Verify: `GroupMetadataControllerTests`.
- Data: new `AudiobookField` table via `CREATE TABLE IF NOT EXISTS` (existing databases get it at startup), which cascade-deletes with its `Audiobook`. Values are JSON (`"text"`, `["a","b"]`, `null`) so list and text fields share one table.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
All in a new `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMetadataControllerTests.cs`, using the scan helpers from `GroupMatchControllerTests`:
- A fresh book returns the group's title and author with origin `"file"` and the rest null. An unknown group gets 404 on both GET and PUT.
- Picking an Audible candidate fills every field it has, with origin `"provider"` and provider `"audible"`. A field the candidate leaves empty keeps its earlier value.
- `PUT` sets `narrators` and `title` as `"user"`. Picking a different Audible candidate then changes the other fields but leaves those two alone, and a later `GET` still shows them.
- After an Audible pick, picking an Open Library candidate fills only fields that were empty or from a file, and leaves the Audible values alone.
- `PUT` with a blank title returns 400 with the `title` message, and a too-long publisher returns 400 with the `publisher` message. Nothing is stored either time.

## Manual checks
- Run `scripts/start-api.ps1`, scan, and pick a match for a book from the book page. `GET /api/groups/{id}/metadata` then shows provider values. `PUT` a narrator, pick another result, and `GET` again: the narrator is unchanged.
- Run a rescan from the dashboard and confirm the values are still there.

## Out of scope
- The editor UI showing current vs proposed values and their origin (next slice, 33)
- Reverting a user value to the provider's value, or clearing a field's origin
- Description, genre, comment, sort fields, track/disc, and the cover (covers are a later phase 5 slice; genres are phase 6)
- Normalized origin (phase 6), and syncing edited title or author back to the group shown on Incoming

## Notes
- File count: 7 (`DatabaseInitializer.cs`, `Audiobook.cs`, `AudiobookRepository.cs`, `MetadataDtos.cs`, `AudiobookService.cs`, `GroupsController.cs`, new `GroupMetadataControllerTests.cs`).
- Split: the editor UI (phase 5's "current vs proposed with origin") is slice 33. Manual completion with no match and covers come after it.
- Shortcut: the group-title fallback is labelled `"file"` even when the user renamed the group on Incoming, because the group doesn't record which correction set `UserCorrected`. Mark it `// shortcut:` in `GetMetadata`. Revisit if it confuses the editor.
- Rule (c) follows the PRD: print data never replaces known audiobook values. A user who wants the Open Library value can type it in, and it becomes `"user"`.
- `seriesPart` stays a string, per decision 2026-10-19 (slice 27). Parsing it into a number waits for naming (phase 7).
- Conflict check: no new package. One current match per book stays as it is. Excluded files have no group, so they never get values.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 169 passed, 0 failed | Was 161 |
| Frontend tests | 157 passed, 0 failed | Unchanged (no client change) |
| New tests | 5 of 5 present, passing | `GroupMetadataControllerTests`: 8 tests cover the 5 listed (fallback + 404, Audible pick + gap, user values survive, Open Library after Audible, 400s) plus 3 extra (blank user value, Open Library replaces file fallback, PUT creates the Audiobook) |
| Manual: pick, PUT narrator, re-pick keeps it | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |
| Manual: rescan keeps values | Not run | Same |

Data checks for the developer (not run): `SELECT Field, ValueJson, Origin, Provider FROM AudiobookField f JOIN Audiobook b ON b.Id = f.AudiobookId WHERE b.GroupId = <id>;`

## Outcome (2026-10-19, commit 2cc975e)

**Status:** Implemented
**Summary:** Books now store metadata values in a new `AudiobookField` table, one row per field, each with an origin (`file`, `provider`, `user`) and, for provider values, the provider. `GET`/`PUT /api/groups/{id}/metadata` read the values and save user edits. `PUT /api/groups/{id}/match` copies the candidate's values in the same transaction: it never changes user values, keeps the existing value when the candidate has none, and never lets Open Library replace Audible.
**Changed:** 7 files: `Services/AudiobookService.cs` (`GetMetadata`, `UpdateMetadata`, `ValuesToCopy`), `Data/AudiobookRepository.cs` (`GetFields`, `SaveFields`, `SaveMatch` with value rows), `Data/DatabaseInitializer.cs`, `Models/Api/MetadataDtos.cs`, `Models/Local/Audiobook.cs`, `Controllers/GroupsController.cs`, new `Tests/Controllers/GroupMetadataControllerTests.cs`.
**Deviations:**
- The PUT body `{ fields: {...} }` binds to `UpdateBookMetadataDto(UpdateBookMetadataFieldsDto Fields)`, a two-record wrapper. A body with no `fields` gets the framework's automatic 400: *fine*.
- A field sent as `null` counts as not sent. Only a blank string or `[]` stores an explicit empty user value: *fine*, and the editor (33) must send `""`/`[]` to clear a field.
- `Clean`'s local `Text`/`People` helpers became static `CleanText`/`CleanPeople`, shared with the PUT: *fine*.
- 3 extra tests (blank user value survives a pick, Open Library replaces the file fallback, PUT creates the Audiobook): *improvement*.
- `ValuesToCopy` reads the current rows before `SaveMatch` opens its transaction. That's harmless for a single user: *debt*, leave it.
**Follow-ups:**
- Slice 33: the editor UI on the book page (current vs proposed, origin badges, editing that sends only changed fields).
- Still open: the evidence line for a picked match (30), `getGroup` 404 vs other failures (29), the group-title "file" label shortcut (32).
