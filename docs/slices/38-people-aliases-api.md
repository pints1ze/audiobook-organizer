# Slice 38: People aliases API

**Phase:** 6, Normalization
**Goal:** the API stores canonical people (authors and narrators) with their aliases and resolves any name variant to one canonical person.

## Done when
- [x] `GET /api/people` returns every person as `{ id, name, aliases: [..] }`, ordered by name (case-insensitive), aliases ordered by text.
- [x] `POST /api/people` with `{ name, aliases }` creates a person and returns 201 with the saved person; `PUT /api/people/{id}` with the same body replaces the name and the full alias list and returns 200; an unknown id returns 404.
- [x] Names and aliases are trimmed with inner whitespace collapsed. A blank name or blank alias returns a 400 validation problem (`name`, `aliases`).
- [x] Every name and alias has a match key: whitespace collapsed, case-insensitive, and a single-comma `Last, First` read as `First Last`. So `Stephen King`, `Stephen  King` and `KING, STEPHEN` share a key. A name or alias whose key already belongs to a different person returns a 400 naming the clash (`"Already used by Stephen King."`). Aliases that share a key with the person's own name or with each other are dropped, not rejected.
- [x] `GET /api/people/resolve?name=` returns the person whose name or alias key matches, or 404. A blank `name` returns 400.
- [x] All four checks pass.

## Read first
- `docs/PRD.md` (Data: Person / PersonAlias) and `overview.md` FR-06 (the Stephen King example)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (`CREATE TABLE IF NOT EXISTS`, AUTOINCREMENT comment) and `Data/SettingsRepository.cs` (Dapper, transaction style)
- `src/AudiobookOrganizer.Api/Controllers/SettingsController.cs` and `Services/SettingsService.cs` (`SettingsSaveResult` + `ValidationProblem` pattern)
- `src/AudiobookOrganizer.Api/Program.cs` (DI registrations)
- `src/AudiobookOrganizer.Api.Tests/Controllers/SettingsControllerTests.cs` (`AudiobookOrganizerWebApplicationFactory` usage)

## Tasks
- [x] **Schema.** In `DatabaseInitializer` add `Person (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE)` and `PersonAlias (PersonId INTEGER NOT NULL REFERENCES Person (Id) ON DELETE CASCADE, Alias TEXT NOT NULL, AliasKey TEXT NOT NULL UNIQUE, PRIMARY KEY (PersonId, AliasKey))`. Verify: `DatabaseInitializerTests` still pass.
- [x] **Match key.** In `Services/PersonService.cs` add `public static string NameKey(string name)`: trim, collapse runs of whitespace to one space, and if the name has exactly one comma, swap `Last, First` to `First Last`; then `ToUpperInvariant()`. A separate `Clean(string)` does the trim + collapse that's stored as display text. Verify: `PersonServiceTests`.
- [x] **Repository.** `Data/PersonRepository.cs`: `List()`, `Get(id)`, `FindByKey(key)` (checks `Person.NameKey` and `PersonAlias.AliasKey`), `Create(name, nameKey, aliases)` and `Update(id, name, nameKey, aliases)` (replaces all aliases), each write in one transaction. Register as a singleton in `Program.cs`.
- [x] **Service + controller.** `PersonService` validates (blank, clash against other people's name and alias keys, drop duplicate aliases) and returns a result record like `SettingsSaveResult` plus a not-found flag. `Controllers/PeopleController.cs` (`api/people`) maps it to 200/201/400/404 with `ValidationProblem`. DTOs `PersonDto(int Id, string Name, IReadOnlyList<string> Aliases)` and `PersonRequest(string? Name, IReadOnlyList<string>? Aliases)` in `Models/Api/PersonDtos.cs` (null aliases = none). Register the service in `Program.cs`.
- [x] **Tests.** Add the tests below and run all four checks.
- Data: two new tables, created by `CREATE TABLE IF NOT EXISTS` at startup; no existing data changes.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Services/PersonServiceTests.cs`: `NameKey` gives one key for `Stephen King`, ` Stephen  King `, `KING, STEPHEN` and `king,stephen`; a name with two commas is not swapped; `Clean` collapses whitespace but keeps case.
- `src/AudiobookOrganizer.Api.Tests/Controllers/PeopleControllerTests.cs`: create with aliases returns 201, then list and resolve (`?name=KING, STEPHEN` and an alias) return it; duplicate aliases of the name are dropped.
- Same file: creating a second person whose name or alias collides with the first returns 400 with "Already used by …"; a blank name returns 400 on `name`.
- Same file: `PUT` replaces the name and aliases (an old alias no longer resolves), a person may keep its own name and aliases without a clash, and `PUT` to an unknown id returns 404.
- Same file: resolve with an unknown name returns 404 and with a blank name returns 400.

## Manual checks
- Run `scripts/start-api.ps1`; `POST /api/people` `{ "name": "Stephen King", "aliases": ["Richard Bachman"] }`, then `GET /api/people/resolve?name=king,%20stephen` and `?name=Richard%20Bachman` both return it.
- Restart the API and check the person is still listed (`GET /api/people`).

## Out of scope
- Genres and genre aliases (next slice, same pattern)
- Merging two people, alias suggestions, deleting a person
- Linking people to books (`AudiobookContributor`) and applying normalization to metadata values (`normalized` origin)
- The management screen (client)

## Notes
- File count: 8 (`DatabaseInitializer.cs`, `PersonRepository.cs`, `PersonService.cs`, `PeopleController.cs`, `PersonDtos.cs`, `Program.cs`, `PersonServiceTests.cs`, `PeopleControllerTests.cs`).
- One `Person` table for authors and narrators, per the PRD (`Person` + `AudiobookContributor` with a role), so a narrator who also writes is one entry.
- Exact-key variants (case, spacing, `Last, First`) resolve automatically; that's lookup, not merging. "Suggest but never auto-merge" applies to fuzzier matches and to joining two existing entries, both in later slices.
- Initials (`J.R.R.` vs `J. R. R.`) and punctuation are not folded into the key; those become aliases or later suggestions.
- Split off for later phase 6 slices: genres + genre aliases API; merge two people (aliases and later book links move to the survivor); alias suggestions; people/genre management screen; resolving a book's author, narrator and genre values to canonical entries.
- Conflict check: no new package; SQLite + Dapper and the `CREATE TABLE IF NOT EXISTS` decision (slice 05); AUTOINCREMENT so ids aren't reused (slice 24 decision); matches PLAN.md phase 6 and PRD Data.
