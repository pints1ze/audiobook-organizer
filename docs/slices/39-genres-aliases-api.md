# Slice 39: Genres aliases API

**Phase:** 6, Normalization
**Goal:** the API stores canonical genres with their aliases and resolves any genre variant to one canonical genre, reusing slice 38's people code for both.

## Done when
- [x] `GET /api/genres`, `POST /api/genres`, `PUT /api/genres/{id}` and `GET /api/genres/resolve?name=` behave exactly like their `/api/people` counterparts (same bodies, ordering, 200/201/400/404 rules, dropped duplicate aliases, "Already used by …" clashes).
- [x] The genre match key is whitespace-collapsed, case-insensitive, and treats `-`, `_` and `/` as spaces, so `Science Fiction`, `science-fiction` and `SCIENCE  FICTION` share a key. There's no `Last, First` swap for genres. `Sci-Fi` only resolves to `Science Fiction` once it's added as an alias.
- [x] People and genres are separate namespaces: a genre may share a key with a person (`Horror` the genre and a person named `Horror`) without a clash.
- [x] Every existing `/api/people` test still passes unchanged.
- [x] All four checks pass.

## Read first
- `docs/slices/38-people-aliases-api.md` (`## Outcome`)
- `src/AudiobookOrganizer.Api/Data/PersonRepository.cs`, `Services/PersonService.cs`, `Controllers/PeopleController.cs`, `Models/Api/PersonDtos.cs`
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (the `Person` / `PersonAlias` tables)
- `src/AudiobookOrganizer.Api.Tests/Controllers/PeopleControllerTests.cs` and `Services/PersonServiceTests.cs`

## Tasks
- [x] **Schema.** In `DatabaseInitializer` add `Genre (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE)` and `GenreAlias (GenreId INTEGER NOT NULL REFERENCES Genre (Id) ON DELETE CASCADE, Alias TEXT NOT NULL, AliasKey TEXT NOT NULL UNIQUE, PRIMARY KEY (GenreId, AliasKey))`, mirroring the person tables.
- [x] **One repository for both.** Rename `Data/PersonRepository.cs` to `Data/CanonicalNameRepository.cs`. Add `public enum CanonicalKind { Person, Genre }`; every method takes the kind first and picks its tables from a fixed private map (`Person`/`PersonAlias`/`PersonId`, `Genre`/`GenreAlias`/`GenreId`). Table names come only from that map, never from input. Rename `PersonAliasRow` to `AliasRow` (public) and the private row records to match.
- [x] **One service for both.** Rename `Services/PersonService.cs` to `Services/CanonicalNameService.cs` (`PersonSaveResult` → `CanonicalSaveResult`). Each public method takes a `CanonicalKind`. Keep `Clean` and `NameKey` (people) and add `GenreKey`: `Clean`, replace `-`, `_` and `/` with spaces, `Clean` again, `ToUpperInvariant()`. A private `KeyFor(kind, text)` picks between them. Rename `Models/Api/PersonDtos.cs` to `CanonicalEntryDtos.cs` with `CanonicalEntryDto(int Id, string Name, IReadOnlyList<string> Aliases)` and `CanonicalEntryRequest(string? Name, IReadOnlyList<string>? Aliases)`; the JSON shape doesn't change. Update the registrations in `Program.cs`.
- [x] **Controllers.** `PeopleController` passes `CanonicalKind.Person`. Add `Controllers/GenresController.cs` (`api/genres`) as the same thin controller with `CanonicalKind.Genre`. Keep each controller's own resolve error text ("Enter a name to resolve." / "Enter a genre to resolve.").
- [x] **Tests.** Rename `Services/PersonServiceTests.cs` to `CanonicalNameServiceTests.cs` (class name too) and add the genre key tests; add `Controllers/GenresControllerTests.cs`. Run all four checks.
- Data: two new tables, created by `CREATE TABLE IF NOT EXISTS` at startup; existing person rows are untouched.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Services/CanonicalNameServiceTests.cs`: `GenreKey` gives one key for `Science Fiction`, `science-fiction`, ` SCIENCE  FICTION `, `Science_Fiction` and `Science/Fiction`; `GenreKey("King, Stephen")` keeps the comma order (no swap). Existing `NameKey`/`Clean` tests still pass.
- `src/AudiobookOrganizer.Api.Tests/Controllers/GenresControllerTests.cs`: create `Science Fiction` with alias `Sci-Fi` returns 201, then list and `resolve?name=science-fiction` and `?name=sci%20fi` return it.
- Same file: a second genre whose name or alias clashes returns 400 "Already used by Science Fiction."; `PUT` replaces the aliases; `PUT` to an unknown id returns 404; blank resolve returns 400 and unknown returns 404.
- Same file: a person and a genre with the same name both save (no cross-kind clash).

## Manual checks
- Run `scripts/start-api.ps1`; `POST /api/genres` `{ "name": "Science Fiction", "aliases": ["Sci-Fi"] }`, then `GET /api/genres/resolve?name=science-fiction` and `?name=SCI-FI` both return it, and `GET /api/people` is unaffected.

## Out of scope
- Merging, suggestions, deleting entries (next slices)
- Linking genres to books (`AudiobookGenre`) or resolving a book's genre values
- The management screen

## Notes
- File count: 9 (`DatabaseInitializer.cs`, `CanonicalNameRepository.cs`, `CanonicalNameService.cs`, `CanonicalEntryDtos.cs`, `PeopleController.cs`, `GenresController.cs`, `Program.cs`, `CanonicalNameServiceTests.cs`, `GenresControllerTests.cs`), three of them renames. Slightly over "about 8", but the alternative (copying slice 38's four files for genres) is more code and two copies of the clash rules.
- The rename is the reuse rung of AGENTS.md section 4, not an unrelated refactor: genres are the second use of the same logic.
- `-` → space means `Sci-Fi` and `Sci Fi` share a key, but `Sci-Fi` and `Science Fiction` don't; that's what aliases (and later suggestions) are for.
- Remaining phase 6 pieces after this: merge two entries; alias suggestions; the management screen (people and genres: create, edit, merge, accept a suggestion).
- Resolving a book's author, narrator and genre values to canonical entries isn't in phase 6's done-when lines; it's raised at the end of the phase for the developer to place (likely phase 7, before naming).
- Conflict check: no new package; SQLite + Dapper, `CREATE TABLE IF NOT EXISTS`, AUTOINCREMENT ids; the slice 38 decision that a key belongs to one entry (now per kind).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 217 passed, 0 failed | Was 200; `PeopleControllerTests` unchanged and passing |
| Frontend tests | 195 passed, 0 failed | Unchanged (no client changes) |
| New tests | 4 of 4 present, passing | `CanonicalNameServiceTests` (genre key variants, no comma swap) and `GenresControllerTests` (create/list/resolve, clash 400, PUT replace/404, resolve 400/404, no cross-kind clash): 33 tests across the three people/genre files |
| Manual: POST Science Fiction + Sci-Fi, resolve `science-fiction` and `SCI-FI`, people unaffected | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |

Data checks for the developer (not run): `SELECT g.Id, g.Name, g.NameKey, a.Alias, a.AliasKey FROM Genre g LEFT JOIN GenreAlias a ON a.GenreId = g.Id;`

## Outcome (2026-10-19, commit f51cd34)

**Status:** Implemented
**Summary:** Genres have their own `Genre` / `GenreAlias` tables and `GET`/`POST /api/genres`, `PUT /api/genres/{id}` and `GET /api/genres/resolve?name=`, with the same rules as people. People and genres now share one `CanonicalNameRepository` and `CanonicalNameService` keyed by `CanonicalKind`; genres use `GenreKey` (case, spacing and `-`/`_`/`/` folded, no comma swap).
**Changed:** 11 code files: `Data/CanonicalNameRepository.cs`, `Services/CanonicalNameService.cs`, `Models/Api/CanonicalEntryDtos.cs` (replacing the `Person*` files), `Controllers/GenresController.cs` (new), `PeopleController.cs`, `DatabaseInitializer.cs`, `Program.cs`; tests `Services/CanonicalNameServiceTests.cs` (replacing `PersonServiceTests.cs`), `Controllers/GenresControllerTests.cs` (new). Plus this slice file and `docs/PLAN.md`.
**Deviations:**
- 11 files vs the slice's estimate of 9: the estimate counted each rename once, but git counts the old and new file separately. The task list didn't change: *fine*.
- Git records the renames as a delete plus an add because so much of each file changed: *fine*.
- `GenresController` repeats `PeopleController`'s thin body instead of sharing a base class: *fine*. Two small controllers don't justify a base class.
- Tests beyond the list (`Science - Fiction`, genre ordering, dropped duplicate aliases, blank 400, people keys unaffected by genre folding): *improvement*.
**Follow-ups:**
- Remaining phase 6 pieces: merge two entries, alias suggestions, the management screen.
- The slice 38 race debt (UNIQUE clash returns 500) now applies to both kinds.
- Manual check for 39 not run.
