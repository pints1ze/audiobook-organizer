# Slice 40: Merge entries API

**Phase:** 6, Normalization
**Goal:** the user can merge one canonical person or genre into another through the API, so its name and aliases become aliases of the one that's kept.

## Done when
- [x] `POST /api/people/{id}/merge` with `{ fromId }` folds person `fromId` into person `id`: `fromId`'s name and all its aliases become aliases of `id`, `fromId` is deleted, and the response is 200 with the kept entry. `POST /api/genres/{id}/merge` does the same for genres.
- [x] After a merge, resolving the merged entry's old name or any of its old aliases returns the kept entry; `GET` lists only the kept one.
- [x] A missing `id` or `fromId` (including an id of the other kind) returns 404; `fromId` equal to `id`, or no `fromId`, returns a 400 validation problem on `fromId`.
- [x] The merge is one transaction: on any failure neither entry changes.
- [x] All four checks pass.

## Read first
- `docs/slices/39-genres-aliases-api.md` (`## Outcome`)
- `src/AudiobookOrganizer.Api/Data/CanonicalNameRepository.cs` (`TableMap`, `Update`, `InsertAliases`, private `Get`)
- `src/AudiobookOrganizer.Api/Services/CanonicalNameService.cs` (`CanonicalSaveResult`, `Update`)
- `src/AudiobookOrganizer.Api/Controllers/PeopleController.cs`, `GenresController.cs` (`SaveFailed`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/PeopleControllerTests.cs`, `GenresControllerTests.cs`

## Tasks
- [x] **Repository.** Add `Merge(CanonicalKind kind, long intoId, long fromId)` to `CanonicalNameRepository`: in one transaction check both rows exist (return null if not), re-point `fromId`'s alias rows to `intoId` (`UPDATE {Alias} SET {ForeignKey} = @intoId WHERE {ForeignKey} = @fromId`), insert `fromId`'s `Name`/`NameKey` as an alias of `intoId`, delete the `fromId` entry, commit, return the kept entry. Keys can't clash: the service already keeps every key unique within a kind.
- [x] **Service.** Add `Merge(CanonicalKind kind, long id, long? fromId)` to `CanonicalNameService`, returning `CanonicalSaveResult`: a missing or equal `fromId` gives an error on `fromId` ("Choose another entry to merge in."); a null repository result gives `NotFound`. Add `public const string FromIdField = "fromId"`.
- [x] **API.** Add `MergeRequest(long? FromId)` to `Models/Api/CanonicalEntryDtos.cs`. Add `[HttpPost("{id:long}/merge")]` to `PeopleController` and `GenresController`, mapping the result through the existing `SaveFailed`.
- [x] **Tests.** Add the tests below to the two existing controller test files and run all four checks.
- Data: none (no schema change; merges move rows between existing tables).
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Controllers/PeopleControllerTests.cs`: merging `Richard Bachman` (alias `R. Bachman`) into `Stephen King` returns 200 with aliases including both; resolving `Richard Bachman` and `R. Bachman` returns Stephen King and the list has one person.
- Same file: merge with an unknown `fromId` or unknown `id` returns 404; merging an entry into itself or with no `fromId` returns 400 on `fromId`.
- `src/AudiobookOrganizer.Api.Tests/Controllers/GenresControllerTests.cs`: merging `Sci-Fi` into `Science Fiction` makes `resolve?name=sci-fi` return Science Fiction; a person id used as `fromId` on `/api/genres/{id}/merge` returns 404.

## Manual checks
- Run `scripts/start-api.ps1`; create two people, `POST /api/people/{keptId}/merge` `{ "fromId": <otherId> }`, then `GET /api/people` shows one entry with the other's name as an alias.

## Out of scope
- Undoing a merge (the old name is now an alias; the user can edit it out and recreate the entry)
- Moving book links (none exist yet; the linking slice must move them inside the same transaction)
- Suggestions and the management screen

## Notes
- File count: 7 (`CanonicalNameRepository.cs`, `CanonicalNameService.cs`, `CanonicalEntryDtos.cs`, `PeopleController.cs`, `GenresController.cs`, `PeopleControllerTests.cs`, `GenresControllerTests.cs`).
- A merge is always the user's explicit action; nothing calls it automatically (PRD: "never merges them on its own").
- `fromId` from the other kind is a 404 because each kind's tables are separate.
- Remaining phase 6 pieces after this: alias suggestions API; the management screen (list, create, edit, merge, suggestions).
- Conflict check: matches the slice 38/39 decisions (one key per entry within a kind) and the PRD; no new package.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 222 passed, 0 failed | Was 217 |
| Frontend tests | 195 passed, 0 failed | Unchanged (no client changes) |
| New tests | 3 of 3 present, passing | 5 merge tests: people merge + resolve, 404s, 400s on `fromId`; genres merge + resolve, cross-kind `fromId` 404 |
| Manual: merge two people via the running API, one entry with the other's name as alias | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |

Data checks for the developer (not run): `SELECT p.Id, p.Name, a.Alias FROM Person p LEFT JOIN PersonAlias a ON a.PersonId = p.Id;` (the merged entry's row is gone and its name is an alias).

## Outcome (2026-10-19, commit 92af862)

**Status:** Implemented
**Summary:** `POST /api/people/{id}/merge` and `POST /api/genres/{id}/merge` with `{ fromId }` fold one entry into another in one transaction: its aliases move over, its name becomes an alias and the entry is deleted. Missing entries (including the other kind's ids) return 404; a missing or self `fromId` returns 400.
**Changed:** 7 code files: `Data/CanonicalNameRepository.cs` (`Merge`), `Services/CanonicalNameService.cs` (`Merge`, `FromIdField`), `Models/Api/CanonicalEntryDtos.cs` (`MergeRequest`), `PeopleController.cs`, `GenresController.cs`, and 5 tests across `PeopleControllerTests.cs` and `GenresControllerTests.cs`. Plus this slice file, `docs/PLAN.md` and `docs/CURRENT-STATE.md`.
**Deviations:**
- The "one transaction" item is met in code (one transaction, early return before any write) but no test forces a mid-merge failure: *fine*; the 404 test checks that nothing changes.
- Aliases are re-pointed before the merged entry is deleted, so the `ON DELETE CASCADE` doesn't take them: *fine*, noted for the book-link slice, which must move links before the delete too.
**Follow-ups:**
- Remaining phase 6 pieces: alias suggestions API; the management screen (list, create, edit, merge, accept a suggestion).
- When books link to people and genres, `Merge` must move those links in the same transaction.
- Manual check for 40 not run.
