# Slice 46: Book people links API

**Phase:** 7, Naming & preview
**Goal:** `GET /api/groups/{id}/people` shows, for each of a book's authors and narrators, the canonical person it resolves to through the alias catalog, or likely people to link it to when it doesn't resolve.

## Done when
- [x] `GET /api/groups/{id}/people` returns `{ groupId, authors: [...], narrators: [...] }`, one row per stored name in order: `{ name, entry, suggestions }`. Unknown group → 404; a book with no names → empty lists
- [x] a name whose `NameKey` matches a person's name or alias has `entry` set to that person (with aliases) and no suggestions; this includes `Last, First` and spacing or case variants, and a group's file-fallback author
- [x] an unresolved name has `entry: null` and `suggestions`: every person that the existing same-letters or initials rule pairs with the name, each with its reason, ordered by name. Nothing is ever linked or created by this endpoint
- [x] after a client adds the name as an alias (`PUT /api/people/{id}`) or creates the person (`POST /api/people`), the same GET shows it resolved; renaming or merging that person changes the resolved `entry`, with no book data rewritten
- [x] all four checks pass

## Read first
- `docs/CURRENT-STATE.md` decisions for slices 38–41 and the 2026-10-19 developer decision on linking (in Notes below)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs`: `GetMetadata` (where authors and narrators come from, including the file fallback)
- `src/AudiobookOrganizer.Api/Services/CanonicalNameService.cs` (`Resolve`, `NameKey`) and `Services/CanonicalSuggestions.cs` (the two rules)
- `src/AudiobookOrganizer.Api/Controllers/GroupsController.cs`, `Models/Api/CanonicalEntryDtos.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMetadataControllerTests.cs` (scan + pick-match setup) and `Services/CanonicalSuggestionsTests.cs`

## Tasks
- [x] **DTOs.** `Models/Api/CanonicalEntryDtos.cs`: `NameSuggestionDto(CanonicalEntryDto Entry, string Reason)`, `NameLinkDto(string Name, CanonicalEntryDto? Entry, IReadOnlyList<NameSuggestionDto> Suggestions)`, `BookPeopleDto(long GroupId, IReadOnlyList<NameLinkDto> Authors, IReadOnlyList<NameLinkDto> Narrators)`.
- [x] **Suggestions for one name.** `Services/CanonicalSuggestions.cs`: `public static IReadOnlyList<NameSuggestionDto> ForName(CanonicalKind kind, string name, IReadOnlyList<CanonicalEntryDto> entries)`. It reuses the same-letters rule and (for people) the initials rule against each entry's name and aliases, first rule wins per entry, ordered by entry name then id. Refactor the private rule helpers so `Suggest` and `ForName` share them; `Suggest`'s output and existing tests stay unchanged.
- [x] **Service and endpoint.** `Services/AudiobookService.cs` takes `CanonicalNameService` too and gains `BookPeopleDto? GetPeople(long groupId)`: null for an unknown group; reads the author and narrator values from `GetMetadata` (so the file fallback counts); resolves each with `CanonicalNameService.Resolve(CanonicalKind.Person, name)`; for unresolved names calls `ForName` against the person list, loaded once per request. `Controllers/GroupsController.cs`: `[HttpGet("{id:long}/people")]` → 200 or 404.
- [x] **Tests.** `Tests/Services/CanonicalSuggestionsTests.cs` and a new `Tests/Controllers/GroupPeopleControllerTests.cs` (below). Run all four checks.
- Data: none. No new tables or columns: a link is an alias match computed on each read.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `CanonicalSuggestionsTests.cs`: `ForName` returns a same-letters match (`"J.R.R. Tolkien"` vs entry `"JRR Tolkien"`) and an initials match (`"B. Sanderson"` vs `"Brandon Sanderson"`) with reasons; an unrelated name returns none; a matching alias counts; genres get only the same-letters rule.
- `GroupPeopleControllerTests.cs`: unknown group → 404; a scanned group with no match returns its file-fallback author as one author row.
- `GroupPeopleControllerTests.cs`: after picking a match (authors `Ann Author`, narrators `Ned Narrator`, `Nia Narrator`) with person `Ann Author` having alias `Author, Ann`: the author row has that entry and no suggestions; with a person `N. Narrator`, both narrator rows have `entry: null` and list `N. Narrator` as a suggestion with the initials reason.
- `GroupPeopleControllerTests.cs`: adding `Ned Narrator` as an alias through `PUT /api/people/{id}` makes the next GET resolve it; renaming that person changes `entry.name` in the GET.

## Manual checks
- With the API running, pick a match on a book page, then `GET /api/groups/{id}/people` in the browser: names known on `/people` show their entry; others list suggestions or none. Add one as an alias on `/people` and reload the GET: it resolves.

## Out of scope
- The book page showing links, suggestions and "Link to…"/"Add as new person" (next slice; it uses the existing `PUT`/`POST /api/people`)
- A genres field on books and genre links (a later phase 7 slice)
- Changing `GET /api/groups/{id}/metadata` or its `origin` values: the people response is where a value shows as normalized
- Book-to-token mapping and the preview

## Notes
- File count: 6 (`CanonicalEntryDtos.cs`, `CanonicalSuggestions.cs`, `AudiobookService.cs`, `GroupsController.cs`, `CanonicalSuggestionsTests.cs`, new `GroupPeopleControllerTests.cs`). Under the cap; `Program.cs` doesn't change because `AudiobookService` is resolved from DI.
- Why next: PLAN.md's phase 7 order puts linking (API, then book page) after templates in settings, and naming needs canonical names.
- Developer decisions 2026-10-19 (asked while writing this slice): (1) links are resolved through aliases on each read, with no link tables; linking a name means adding it as a person's alias or creating the person, so merges and renames carry through. The PRD's `AudiobookContributor`/`AudiobookGenre` line is updated to match. (2) People first; genres get a book field and links in a later phase 7 slice.
- A name equal to an entry's own name resolves too (`FindByKey` checks names and aliases). Duplicate names in a list each get a row.
- Suggestions are computed over the whole person list per request, like slice 41 (same O(n) per name shortcut scale).
- Conflict check: keeps "exact-key variants resolve automatically; fuzzy matches are only ever suggested" (slice 38) and "merges only on explicit request" (slice 40). No new packages.

## Test results (2026-10-19)

**Regressions:** none caused by this slice. One intermittent frontend failure, in code this slice doesn't touch (see below)

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 319 passed, 0 failed | Was 311; `--filter GroupPeople\|CanonicalSuggestions` gives 27 passed |
| Frontend tests | First run: 216 passed, 1 failed. 14 reruns: 217 passed, 0 failed | `NamesPage.test.tsx > NamesPage > merges through the row control` failed once and then passed 14 times in a row (1 file run, 13 full runs). The slice changed no client files (`2838374` touches 6 backend files), so it's a flaky test. The failure message wasn't captured. Not accepted in CURRENT-STATE.md |
| New tests | 4 of 4 present, passing | `CanonicalSuggestionsTests.cs`: 4 `ForName` tests. `GroupPeopleControllerTests.cs`: 404, file-fallback author (`king,  stephen` resolves), resolved author plus initials suggestions for narrators, alias-then-rename resolves |
| Manual: GET `/api/groups/{id}/people` before and after adding an alias | Not run | API not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |

Data checks: none (no schema change; links are computed on read).

## Outcome (2026-10-19, commit 2838374)

**Status:** Implemented
**Summary:** `GET /api/groups/{id}/people` lists a book's authors and narrators (file fallback included), each resolved by `NameKey` through the person catalog or, when unresolved, with the people the same-letters or initials rule pairs it with. `CanonicalSuggestions.ForName` shares the rule helpers with `Suggest`. Nothing is stored: a link is an alias match on each read, so adding an alias, renaming or merging carries through.
**Changed:** 6 files: `CanonicalEntryDtos.cs` (3 DTOs), `CanonicalSuggestions.cs` (`ForName`, rules take name lists), `AudiobookService.cs` (`GetPeople`, takes `CanonicalNameService`), `GroupsController.cs`, `CanonicalSuggestionsTests.cs` (+4), new `GroupPeopleControllerTests.cs` (4). Plus `docs/PRD.md` (data-model line, developer decision) and the PLAN line.
**Deviations:**
- The no-match test sets the group author to `king,  stephen` through `PUT /api/groups/{id}` rather than relying on the fixture's author; it also covers the `Last, First` and spacing variant: *fine*.
- Merge carrying through isn't tested directly (only rename); it uses the same alias lookup: *debt*, small. Add a merge case when the book page slice touches these tests, or leave it.
- The person list is loaded only when some name doesn't resolve (the slice said once per request): *improvement*.
- The flaky frontend test from the test run was a race in `NamesPage.test.tsx` (effect-driven focus checked straight after `findByText`; reproduced by delaying the effect one tick). Fixed separately in `2a17eef` (`fix:`, three assertions now `await waitFor`), after which 5 full frontend runs gave 217/217: *fine* (developer's OK).
**Follow-ups:**
- Next: people links on the book page (show the canonical name as normalized, suggestions, "Link to…" via `PUT /api/people/{id}`, "Add as new person" via `POST /api/people`), plus the client types and `api.ts` call for this endpoint.
- Then a hand-entered genres field on books, linked the same way.
- Manual GET check not run (API down); carried to the phase 7 verification.
