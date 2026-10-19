# Slice 41: Merge suggestions API

**Phase:** 6, Normalization
**Goal:** the API suggests pairs of people or genres that are probably the same entry, without ever merging them.

## Done when
- [x] `GET /api/people/suggestions` and `GET /api/genres/suggestions` return `[{ keep, merge, reason }]`, where `keep` and `merge` are full entries (`{ id, name, aliases }`) and `reason` is a sentence for the user. Each pair of entries appears at most once. An empty catalog returns `[]`.
- [x] **Same letters** (both kinds): two entries are suggested when any name or alias of one has the same letters-and-digits-only key as any name or alias of the other, ignoring case. `J.R.R. Tolkien` / `J. R. R. Tolkien` and `SciFi` / `Sci-Fi` are suggested. Reason: `"J.R.R. Tolkien" and "J. R. R. Tolkien" differ only in spacing or punctuation.`
- [x] **Initials** (people only): two names are suggested when they have the same last word, the same number of words, and each earlier word is equal or one is a single letter (with or without a period) matching the other's first letter. `S. King` / `Stephen King` and `J R R Tolkien` / `John Ronald Reuel Tolkien` are suggested; `Stephen King` / `Tabitha King` and `S. King` / `Stephen Edwin King` are not. Reason: `"S. King" may be "Stephen King" with initials.`
- [x] `keep` is the entry with more aliases, then the lower id. Results are ordered by `keep` name, then `merge` name (case-insensitive). Calling the endpoint changes nothing.
- [x] All four checks pass.

## Read first
- `docs/slices/40-merge-entries-api.md` (`## Outcome`)
- `src/AudiobookOrganizer.Api/Services/CanonicalNameService.cs` (`Clean`, `NameKey`, `GenreKey`, `List`)
- `src/AudiobookOrganizer.Api/Models/Api/CanonicalEntryDtos.cs`
- `src/AudiobookOrganizer.Api/Controllers/PeopleController.cs`, `GenresController.cs`
- `src/AudiobookOrganizer.Api/Services/Grouping/` (an existing pure, deterministic engine with a test file beside it, as a style reference)

## Tasks
- [x] **Pure suggester.** Create `Services/CanonicalSuggestions.cs` with `public static IReadOnlyList<MergeSuggestionDto> Suggest(CanonicalKind kind, IReadOnlyList<CanonicalEntryDto> entries)` implementing the two rules, the `keep` choice, de-duplication (first rule found per pair wins; same-letters is checked first) and ordering. No database access. Verify: `CanonicalSuggestionsTests`.
- [x] **DTO.** Add `MergeSuggestionDto(CanonicalEntryDto Keep, CanonicalEntryDto Merge, string Reason)` to `Models/Api/CanonicalEntryDtos.cs`.
- [x] **Service + API.** Add `Suggestions(CanonicalKind kind)` to `CanonicalNameService` (`CanonicalSuggestions.Suggest(kind, repository.List(kind))`). Add `[HttpGet("suggestions")]` to `PeopleController` and `GenresController`.
- [x] **Tests.** Add the tests below and run all four checks.
- Data: none (read-only).
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Services/CanonicalSuggestionsTests.cs`: same-letters pairs (`J.R.R. Tolkien` / `J. R. R. Tolkien`, a match through an alias) are suggested with the spacing reason; unrelated names aren't; each pair appears once even when several names match.
- Same file: initials pairs (`S. King` / `Stephen King`, `J R R Tolkien` / `John Ronald Reuel Tolkien`) are suggested for people; `Stephen King` / `Tabitha King` and `S. King` / `Stephen Edwin King` aren't; the initials rule doesn't apply to genres.
- Same file: `keep` is the entry with more aliases, then the lower id; results are ordered by `keep` then `merge` name.
- `src/AudiobookOrganizer.Api.Tests/Controllers/PeopleControllerTests.cs`: after creating `Stephen King` and `S. King`, `GET /api/people/suggestions` returns one suggestion with both entries; with no people it returns `[]`; `GET /api/people` is unchanged afterwards.

## Manual checks
- Run `scripts/start-api.ps1`; create `J.R.R. Tolkien` and `J. R. R. Tolkien` as people, then `GET /api/people/suggestions` lists the pair and `GET /api/people` still shows two entries.

## Out of scope
- Dismissing a suggestion ("these are different people"): a dismissed pair would come back on the next call; noted for the screen slice
- Suggesting from names in book metadata that don't resolve yet (see Notes)
- Fuzzy or edit-distance matching, nicknames (`Bob` / `Robert`)
- The management screen

## Notes
- File count: 7 (`CanonicalSuggestions.cs`, `CanonicalEntryDtos.cs`, `CanonicalNameService.cs`, `PeopleController.cs`, `GenresController.cs`, `CanonicalSuggestionsTests.cs`, `PeopleControllerTests.cs`).
- Suggestions are computed on each request from the full list: fine for a personal catalog of hundreds or a few thousand entries (the initials rule is pairwise). `// shortcut:` comment if it's O(n²) — revisit when a catalog has more than a few thousand entries.
- This meets "the app suggests but never auto-merges aliases" for entries already in the catalog. Book metadata has `authors` and `narrators` but no genres yet (genres are manual in v1), and nothing yet links book values to entries. Whether the screen should also gather unresolved names from books is raised at the end of the phase with "resolving book values".
- Remaining phase 6 piece after this: the management screen (list, create, edit, merge, suggestions), likely two slices (people page; genres reusing it plus suggestions).
- Conflict check: read-only, matches the "never auto-merge" PRD rule and the slice 40 merge semantics (`keep` maps to the merge `id`, `merge` to `fromId`); no new package.
