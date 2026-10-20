# Slice 48: Book genres API

**Phase:** 7, Naming & preview
**Goal:** a book stores a hand-entered genres list through the metadata API, and `GET /api/groups/{id}/genres` shows each genre resolved to its canonical genre or with suggested genres, like the people links.

## Done when
- [x] `GET /api/groups/{id}/metadata` has a `genres` field (`{ value: string[], origin, provider }`): `[]` with a null origin until set; there's no file fallback
- [x] `PUT /api/groups/{id}/metadata` with `fields.genres` stores it as a user value, trimmed with blanks dropped, with the same 50-entry and length limits as names (400 under `genres`); `[]` is an explicit empty user value and a missing `genres` leaves it alone
- [x] picking a match never changes `genres` (providers don't supply genres in v1)
- [x] `GET /api/groups/{id}/genres` returns `{ groupId, genres: [{ name, entry, suggestions }] }` in stored order: a genre whose `GenreKey` matches a genre's name or alias has its `entry`; others have `entry: null` and the same-letters suggestions only. Unknown group → 404. Nothing is linked or created
- [x] all four checks pass

## Read first
- `docs/slices/46-book-people-links-api.md` (the pattern this copies) and its Outcome
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs`: `GetMetadata`, `GetPeople`, `UpdateMetadata`, `ValuesToCopy`, `Fields`, `CleanPeople`
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs` and `Models/Api/CanonicalEntryDtos.cs`
- `src/AudiobookOrganizer.Api/Services/CanonicalSuggestions.cs` (`ForName` with `CanonicalKind.Genre` uses only the same-letters rule)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMetadataControllerTests.cs` and `GroupPeopleControllerTests.cs` (setup helpers)

## Tasks
- [x] **DTOs.** `Models/Api/MetadataDtos.cs`: add `MetadataFieldDto<IReadOnlyList<string>> Genres` to `BookMetadataFieldsDto` (after `Narrators`) and `IReadOnlyList<string>? Genres = null` to `UpdateBookMetadataFieldsDto`. `Models/Api/CanonicalEntryDtos.cs`: `BookGenresDto(long GroupId, IReadOnlyList<NameLinkDto> Genres)`.
- [x] **Stored field.** `Services/AudiobookService.cs`: `Fields.Genres = "genres"`; `GetMetadata` reads it with the list reader and no fallback; `UpdateMetadata` sets it through the same list cleaning as people (`SetPeople`/`CleanPeople`, unchanged names and messages; don't rename them). `ValuesToCopy` doesn't list genres, so a pick leaves them alone.
- [x] **Links.** `AudiobookService.GetGenres(long groupId)`: null for an unknown group; resolves each stored genre with `CanonicalNameService.Resolve(CanonicalKind.Genre, name)` and, for unresolved ones, `CanonicalSuggestions.ForName(CanonicalKind.Genre, …)` against the genre list loaded once on first need. Share the resolve-or-suggest helper with `GetPeople` (parameterized by kind) rather than copying it. `Controllers/GroupsController.cs`: `[HttpGet("{id:long}/genres")]` → 200 or 404.
- [x] **Tests.** Add to `Tests/Controllers/GroupMetadataControllerTests.cs` and a new `Tests/Controllers/GroupGenresControllerTests.cs` (below). Run all four checks.
- Data: none. Genres are one more `AudiobookField` row (`field = 'genres'`, JSON list); no schema change.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `GroupMetadataControllerTests.cs`: a new book's `genres` is `[]` with a null origin; a PUT with `genres: [" Fantasy ", "", "Epic Fantasy"]` stores `["Fantasy", "Epic Fantasy"]` with origin `user`, and a later PUT without `genres` leaves them.
- `GroupMetadataControllerTests.cs`: 51 genres → 400 with a `genres` error; picking a match after setting genres keeps them.
- `GroupGenresControllerTests.cs`: unknown group → 404; with genre `Science Fiction` having alias `SF` and genres `["sf", "science-fiction", "Sciencefiction", "Horror"]` stored: the first two resolve to `Science Fiction`, `Sciencefiction` is unresolved with `Science Fiction` suggested (same-letters reason), `Horror` has no entry and no suggestions.
- `GroupGenresControllerTests.cs`: adding `Horror` through `POST /api/genres` makes the next GET resolve it.

## Manual checks
- With the API running, `PUT /api/groups/{id}/metadata` with `{ "fields": { "genres": ["Sci-Fi"] } }`, then `GET /api/groups/{id}/genres`: unresolved unless `/genres` has a `Sci-Fi` alias; add one on `/genres` and the GET resolves it.

## Out of scope
- The client: `genres` in the TS metadata types, the editor field and a Genres links section on the book page (next slice). The client ignores the extra JSON field until then
- Provider genres (deferred decision: manual in v1)
- A `%genre%` naming token and book-to-token mapping (later phase 7 slice)

## Notes
- File count: 6 (`MetadataDtos.cs`, `CanonicalEntryDtos.cs`, `AudiobookService.cs`, `GroupsController.cs`, `GroupMetadataControllerTests.cs`, new `GroupGenresControllerTests.cs`). Under the cap.
- Why next: slice 47's Outcome and PLAN.md's phase 7 order (genres field and links, then token mapping); phase 7 done-when needs genres linked.
- The client types don't get `genres` here, so `BookMetadataFields` in TS lags one slice; `UpdateBookMetadataFields` never sends it, so a client save leaves genres alone. Check existing backend tests that compare the whole fields object; update their expectations for the new field rather than weakening them.
- Conflict check: matches "Genres are entered by hand in v1", the developer's 2026-10-19 linking decision (aliases resolved on read, no link tables, genres get a book field) and slice 39's `GenreKey`. No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 324 passed, 0 failed | Was 319; `--filter GroupGenres\|GroupMetadata` gives 13 passed |
| Frontend tests | 228 passed, 0 failed | Unchanged (backend-only slice) |
| New tests | 4 of 4 present, passing | `GroupMetadataControllerTests.cs`: cleaned user genres and a PUT without genres keeps them; 51 genres → 400 and a pick keeps genres. `GroupGenresControllerTests.cs`: 404; keys, aliases and same-letters suggestions; adding a genre resolves it |
| Manual: PUT genres `["Sci-Fi"]`, GET `/genres` before and after adding an alias | Not run | API not running. Start with `scripts/start-api.ps1` |

Data checks: none (genres are one more `AudiobookField` row; no schema change).

## Outcome (2026-10-19, commit 58d250a)

**Status:** Implemented
**Summary:** Books have a hand-entered `genres` list in `GET`/`PUT /api/groups/{id}/metadata` (user values only, no file fallback, never touched by a match pick). `GET /api/groups/{id}/genres` resolves each genre by `GenreKey` through the genre catalog or lists same-letters suggestions. `GetPeople` and `GetGenres` share one `Linker(CanonicalKind)` helper.
**Changed:** 6 files: `MetadataDtos.cs` (`Genres` in read and update fields), `CanonicalEntryDtos.cs` (`BookGenresDto`), `AudiobookService.cs` (field, `GetGenres`, `Linker`), `GroupsController.cs`, `GroupMetadataControllerTests.cs` (+2, whole-field expectations include `genres`), new `GroupGenresControllerTests.cs` (3).
**Deviations:**
- Two existing whole-field tests now expect `genres` (`[]`, null origin, also after a pick); nothing weakened: *fine*.
- Genre validation reuses the people messages, so a genre error reads "There can be at most 50 names." / "Each name can be at most … characters.": *debt*, small. Reword to "entries" when the editor shows the genres field (next slice) if it reads oddly there.
**Follow-ups:**
- Next: the client side: `genres` in the TS metadata types, a Genres field in the metadata editor (one per line) and a Genres links section on the book page (link to a suggested genre, add as new genre), reusing `PeopleLinks`' pattern.
- Then book-to-token mapping using canonical names, then the preview endpoint and screen.
- Manual check not run (API down); carried to the phase 7 verification.
