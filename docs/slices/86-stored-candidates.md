# Slice 86: Stored candidates

**Phase:** 11, Parallel identification
**Goal:** a book can be searched on request and its top candidates are stored with a score from title, author and runtime vs the files' duration.

## Done when
- [x] `POST /api/groups/{id}/candidates` searches the providers for the book, stores its top 5 candidates with a score (0–100) and returns them, best first; 404 for an unknown group
- [x] `GET /api/groups/{id}/candidates` returns the stored candidates, the search time and the provider errors; 204 when the book was never searched; 404 for an unknown group
- [x] a failed search (every provider failed, or nothing to search for) is stored as an error with no candidates, and a later successful search replaces it
- [x] candidates are deleted with their group (rescan of an uncorrected group, or a merge that empties one)
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docs/PRD.md` item 12 and the `MetadataCandidate` entity line
- `src/AudiobookOrganizer.Api/Services/MetadataSearchService.cs` (provider order and errors)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`GetMetadata`, for the book's title and authors)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` and `Data/AudiobookRepository.cs` (schema and Dapper style)
- `src/AudiobookOrganizer.Api/Controllers/GroupsController.cs` (`GetMatch`/`PutMatch` shape) and `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMatchControllerTests.cs` and `MetadataControllerTests.cs` (scan seeding and fake providers)

## Tasks
- [x] **Schema.** In `Data/DatabaseInitializer.cs` add `CandidateSearch (GroupId INTEGER PRIMARY KEY REFERENCES FileGroup (Id) ON DELETE CASCADE, SearchedAtUtc TEXT NOT NULL, ErrorsJson TEXT NOT NULL)` and `MetadataCandidate (GroupId INTEGER NOT NULL REFERENCES CandidateSearch (GroupId) ON DELETE CASCADE, Rank INTEGER NOT NULL, Provider TEXT NOT NULL, CandidateJson TEXT NOT NULL, Score INTEGER NOT NULL, PRIMARY KEY (GroupId, Rank))`, both `CREATE TABLE IF NOT EXISTS`. New `Data/CandidateRepository.cs`: `Replace(groupId, searchedAtUtc, errors, scoredCandidates)` in one transaction (delete then insert) and `Get(groupId)`. Verify: the initializer runs twice without error.
- [x] **Score.** New `Services/CandidateService.cs` with a pure `static int Score(string? title, IReadOnlyList<string> authors, long durationMs, MetadataCandidateDto candidate)`: title up to 50 (share of the book's title words found in the candidate's title, words lower-cased with punctuation dropped), author up to 25 (25 when any author matches one of the candidate's after `CanonicalNameService.NameKey`, else 0), runtime up to 25 (25 within 3% of the duration, 12 within 10%, 0 otherwise or when either is missing). Verify with unit tests.
- [x] **Search and store.** `CandidateService.RefreshAsync(groupId, ct)`: keywords are the book's metadata title and first author (from `AudiobookService.GetMetadata`), falling back to the group's `Title`/`Author`, joined with a space, as the pane does. Blank keywords store the error "No title or author to search for." Otherwise call `MetadataSearchService.SearchAsync`; on `MetadataProviderException` store its message as the error; on success score every candidate, keep the top 5 by score (ties keep provider order) and store them with the provider errors. Returns null for an unknown group. Register the repository and service in `Program.cs`.
- [x] **Endpoints.** In `GroupsController.cs`: `GET {id:long}/candidates` → `BookCandidatesDto` / 204 / 404, and `POST {id:long}/candidates` → `BookCandidatesDto` (200, also when the search stored an error) / 404. Add to `Models/Api/MetadataDtos.cs`: `ScoredCandidateDto(MetadataCandidateDto Candidate, int Score)` and `BookCandidatesDto(long GroupId, DateTimeOffset SearchedAtUtc, IReadOnlyList<ScoredCandidateDto> Candidates, IReadOnlyList<string> Errors)`.
- [x] **Tests** (below), then all four checks.
- Data: two new tables, created by `CREATE TABLE IF NOT EXISTS`; cascade from `FileGroup`. No change to existing tables. Nothing is applied to the book: `MetadataMatch` and `AudiobookField` are untouched.
- Auth: none (no auth in the app).
- Accessibility: none (no UI in this slice).

## Tests to add
- `Api.Tests/Services/CandidateServiceTests.cs`: `Score` gives 100 for exact title, author and runtime; drops the runtime part when the runtime is 15% off or missing; gives 0 title points for an unrelated title; ignores case, punctuation and `Last, First` in authors.
- `Api.Tests/Controllers/GroupCandidatesControllerTests.cs`: after a scan, `GET` is 204, `POST` with the Audible fixture returns at most 5 candidates best first with scores and a later `GET` returns the same; an unknown group is 404 for both.
- Same file: with both providers unavailable, `POST` stores and returns the error with no candidates; `POST` again with a working provider replaces it.
- Same file: a rescan that replaces the uncorrected group leaves no `MetadataCandidate` or `CandidateSearch` rows for the old group id.

## Manual checks
- With the API running and a scanned book, `POST /api/groups/{id}/candidates` against the real Audible API returns scored candidates, and the best one is the right book for a known title.

## Out of scope
- Searching in the background after a scan, and the throttle (next slice; the PRD's throttle question is still open).
- Showing candidates in the detail pane, the stage it gives, and batch accept or approve.
- Keeping candidates across a rescan that regroups an uncorrected book (it gets a new group id).

## Notes
- Conflict check: the PRD's v1.1 decision (2026-10-25) reverses slice 27's "candidates aren't stored", so storing them is allowed. A search never applies a match. The open throttle question (`[NEEDS CLARIFICATION]` in the PRD) is outside this slice: one search per request, triggered by hand.
- Uncorrected groups are deleted and recreated on every completed scan (`GroupRepository.ReplaceProposed`), so their candidates go too. The background slice must decide what "new book" means (for example, re-search only groups without a `CandidateSearch` row); raise it then.
- The scoring weights are a first guess; the pane slice may tune them with real results.
- Files touched: `DatabaseInitializer.cs`, `CandidateRepository.cs` (new), `CandidateService.cs` (new), `MetadataDtos.cs`, `GroupsController.cs`, `Program.cs`, `CandidateServiceTests.cs` (new), `GroupCandidatesControllerTests.cs` (new): 8.
- Later slices for this phase: background search after a scan, throttled (needs the throttle answer); candidates side by side in the pane; select books and "Accept suggested match"; select Ready books and approve.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings; built with `-p:OutDir=<scratchpad>` because the running dev API locks `bin/Debug` |
| Typecheck | Passed | |
| Backend tests | 427 passed, 0 failed | same `OutDir` workaround |
| Frontend tests | 349 passed, 0 failed | 20 files |
| New tests | 4 of 4 present, passing | `CandidateServiceTests` (6 tests: score 100, runtime off/missing, no duration, unrelated title, title share, case/punctuation/`Last, First`); `GroupCandidatesControllerTests` (204 then POST top 5 best first and GET; unknown group 404; failed search stored then replaced; rescan removes candidates) |
| Manual: POST candidates against the real Audible API | Not run | the running dev API (started before this slice) doesn't serve the new endpoint; restart it with `scripts/start.ps1 api`, then `POST /api/groups/{id}/candidates` for a scanned book |

## Outcome (2026-10-25, commit ec56cfb)

**Status:** Implemented
**Summary:** `POST /api/groups/{id}/candidates` searches the providers with the book's title and first author, scores each candidate (title words up to 50, author 25, runtime within 3%/10% 25/12) and stores the top 5 with the search time and provider errors in `CandidateSearch` and `MetadataCandidate`; `GET` reads them (204 when never searched). A failed search is stored as an error, and both tables cascade from `FileGroup`.
**Changed:** 9 code files (`Services/CandidateService.cs` and `Data/CandidateRepository.cs` new, `DatabaseInitializer.cs`, `MetadataDtos.cs`, `Models/Local/Audiobook.cs`, `GroupsController.cs`, `Program.cs`, two new test files)
**Done when:** all 5 met; the merge cascade isn't tested on its own (same `DELETE FROM FileGroup` as the rescan test).
**Deviations:**
- Entity classes `CandidateSearch` and `MetadataCandidate` added to `Models/Local/Audiobook.cs` (9 files, not 8): fine, follows the layout rule.
- Title words split on punctuation ("hail-mary" → "hail", "mary") and drop apostrophes: fine, a reading of "punctuation dropped".
- Built and tested with `-p:OutDir=<scratchpad>` because the running dev API locks `bin/Debug`: fine, no code change.
**Follow-ups:**
- Manual check against the real Audible API not run (the dev API predates the slice); do it at phase verification.
- Background search after a scan needs the throttle answer and a definition of "new book" (uncorrected groups get new ids on every rescan).
