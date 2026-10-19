# Slice 28: Open Library fallback

**Phase:** 5, Metadata
**Goal:** when Audible fails or finds nothing, `GET /api/metadata/search` falls back to Open Library, so ISBNs and print-only titles still return candidates.

## Done when
- [x] `GET /api/metadata/search?keywords=...` returns `{ "candidates": [...], "providerErrors": [...] }`. Providers are tried in order (Audible, then Open Library), and the first one that returns at least one candidate wins.
- [x] Audible fails → Open Library is tried; the response is 200 with Open Library's candidates and Audible's error message in `providerErrors`.
- [x] Audible returns nothing (e.g. an ISBN) → Open Library is tried, and `providerErrors` is empty.
- [x] Both providers fail → 502 problem whose detail names both. Both find nothing → 200 with empty lists.
- [x] An ISBN query (10 or 13 digits, hyphens and spaces ignored, ISBN-10 may end in `X`) searches Open Library by `isbn=`, and the candidate carries that normalized ISBN.
- [x] Every test uses fixtures under `docs/fixtures/openlibrary/` (Open Library) or `docs/fixtures/audible/` (Audible) through `FakeHttpMessageHandler`. No test reaches the network.
- [x] All four checks pass.

## Read first
- `docs/slices/27-audible-search.md`, `## Outcome` (the follow-up this slice answers)
- `src/AudiobookOrganizer.Api/Services/AudibleClient.cs`, `Services/IMetadataProvider.cs`, `Models/Api/MetadataDtos.cs`, `Controllers/MetadataController.cs`, `Program.cs`
- `src/AudiobookOrganizer.Api.Tests/Services/FakeHttpMessageHandler.cs`, `Controllers/MetadataControllerTests.cs`, `AudiobookOrganizer.Api.Tests.csproj` (fixture Content items)
- `docs/PRD.md`, Rules: "Audiobook-edition data is preferred over print-edition data"; "A provider failure never blocks manual editing"

## Tasks
- [x] **Capture fixtures.** Save two real, unedited responses from `https://openlibrary.org/search.json?...&limit=20&fields=key,title,subtitle,author_name,first_publish_year,cover_i`:
  - `docs/fixtures/openlibrary/search-project-hail-mary.json`, from `q=project hail mary`
  - `docs/fixtures/openlibrary/search-isbn-9780593135204.json`, from `isbn=9780593135204`

  Add `docs\fixtures\openlibrary\*.json` to the test csproj Content items, the same way the Audible line does. Verify: both parse, and the ISBN one has `numFound` ≥ 1.
- [x] **Open Library client.**
  - Add `Models/OpenLibrary/OpenLibrarySearchResponse.cs` (`docs[]`: `key`, `title`, `subtitle`, `author_name[]`, `first_publish_year`, `cover_i`) and `Services/OpenLibraryClient.cs` (`IMetadataProvider`, `Name` = `"openlibrary"`).
  - Register it as a typed `HttpClient` in `Program.cs`: base address `https://openlibrary.org/`, a 15 s timeout, and a `User-Agent` of `AudiobookOrganizer/1.0 (self-hosted)` (Open Library asks clients to identify themselves).
  - ISBN keywords → `isbn=<digits>`; anything else → `q=<keywords>`; both URL-encoded.
  - Mapping: title, subtitle, authors, `ReleaseDate` = the first publish year as a string, `CoverUrl` = `https://covers.openlibrary.org/b/id/{cover_i}-L.jpg`, and `Isbn` = the normalized query ISBN (otherwise null). ASIN, narrators, series, publisher, language, runtime and format stay null or empty, because Open Library returns work-level print data that mixes editions.
  - Failures are wrapped in `MetadataProviderException` with the message "Open Library search failed: ...", like `AudibleClient`.

  Verify: unit tests against the fixtures.
- [x] **Candidate and result DTOs.** In `Models/Api/MetadataDtos.cs`, add a trailing `string? Isbn = null` to `MetadataCandidateDto`, so `AudibleClient` doesn't change, and add `MetadataSearchResultDto(IReadOnlyList<MetadataCandidateDto> Candidates, IReadOnlyList<string> ProviderErrors)`.
- [x] **Fallback service.**
  - Add `Services/MetadataSearchService.cs`, taking `IEnumerable<IMetadataProvider>`. Register Audible first, then Open Library, both as `IMetadataProvider`, and the service as a singleton or scoped to match the clients' lifetime.
  - It tries providers in order and returns the first non-empty result plus the error messages of the providers that failed before it. If every provider failed, it throws `MetadataProviderException` with the messages joined by a space.
  - Each failure is logged at Warning there, not in the controller.
  - Put the ISBN check here as a `public static bool TryNormalizeIsbn(string, out string)` and use it from `OpenLibraryClient`.

  Verify: controller tests.
- [x] **Endpoint.** In `Controllers/MetadataController.cs`, inject `MetadataSearchService` instead of `IMetadataProvider`, keep the blank-keywords 400 and return `MetadataSearchResultDto`. Update `MetadataControllerTests` so `ClientWith` takes an Audible handler and an Open Library handler. Verify: the integration tests below.
- Data: none. Nothing is stored; `MetadataMatch` comes with the pick-a-match slice.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `Api.Tests/Services/OpenLibraryClientTests.cs`:
  - the title fixture maps to a candidate with provider `openlibrary`, title Project Hail Mary, author Andy Weir, release date `2021` and a `-L.jpg` cover URL;
  - an ISBN query (`978-0-593-13520-4`) sends `isbn=9780593135204` and sets `Isbn` on the candidate;
  - a 503 and a malformed body throw `MetadataProviderException` starting "Open Library search failed".
- `Api.Tests/Controllers/MetadataControllerTests.cs` (update the existing tests for the new shape, then add):
  - Audible fails, Open Library succeeds → 200 with Open Library candidates and one provider error;
  - Audible returns an empty `products` list → Open Library is called and `providerErrors` is empty;
  - both fail → 502 whose detail contains "Audible" and "Open Library";
  - Audible succeeds → Open Library is never called (no requests on its handler).

## Manual checks
- Run `scripts/start-api.ps1`, then open `http://localhost:5070/api/metadata/search?keywords=project%20hail%20mary`. You should get Audible candidates and `providerErrors: []`.
- Open `http://localhost:5070/api/metadata/search?keywords=978-0-593-13520-4`. If Audible returns nothing, you should get Open Library candidates with `isbn` set. If Audible does match the ISBN, you get Audible candidates instead: record which one happened.

## Out of scope
- Search UI, picking a match, storing `MetadataMatch`
- Merging candidates from both providers, or querying both in parallel
- Open Library edition lookups (`/isbn/{isbn}.json`) for publisher, language or pages
- Throttling and caching (the rate-limit question stays open; searches are user-triggered)

## Notes
- File count: 11. Three are trivial (two fixtures and one csproj line), leaving 8 code and test files: OL model, OL client, `MetadataSearchService`, `MetadataDtos`, `MetadataController`, `Program.cs`, `OpenLibraryClientTests`, `MetadataControllerTests`. That's at the cap; `AudibleClient` stays untouched thanks to the defaulted `Isbn`.
- The response shape changes from a bare list to an object. No client code uses it yet.
- Conflict check: no new package. One client per external system in `Services/`, external shapes in `Models/OpenLibrary/`, the logic in a service and a thin controller.
- Leaving publisher and language null on Open Library candidates follows the PRD rule that print data never replaces audiobook values; the editor slice decides what to fill from print data.
- Sample calls on 2026-10-19 returned 200 for `q=project hail mary` (work `/works/OL21745884W`, `cover_i` 11200092) and for `isbn=9780593135204` (1 doc).
- Remaining phase 5 pieces: search UI and picking a candidate (`Audiobook` + `MetadataMatch`); the editor with value origins that keeps user edits; manual completion; covers.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 155 passed, 0 failed | Was 132 |
| Frontend tests | 129 passed, 0 failed | 9 files |
| New tests | 7 of 7 present, passing | `OpenLibraryClientTests` (8 cases), `MetadataControllerTests` (8 cases incl. both-empty), plus `MetadataSearchServiceTests` (12 ISBN cases, added by the implementer) |
| Manual: title search | Passed | Built API run on port 5071 with a scratch DB. `project hail mary` → 200, 50 Audible candidates, `providerErrors: []`. |
| Manual: ISBN search | Passed | `978-0-593-13520-4` → Audible returned nothing, fell back: 200, 1 Open Library candidate, `isbn` 9780593135204, release `2021`, `-L.jpg` cover, `providerErrors: []`. |

## Outcome (2026-10-19, commit 9925b37)

**Status:** Implemented
**Summary:** `MetadataSearchService` tries the registered providers in order (Audible, then the new `OpenLibraryClient`) and returns the first non-empty result plus earlier provider errors. `GET /api/metadata/search` now returns `{ candidates, providerErrors }`, and gives 502 only when every provider failed. ISBN keywords search Open Library by `isbn=` and the candidate carries the ISBN. A live ISBN search fell back to Open Library as designed.
**Changed:** 12 files: `Services/OpenLibraryClient.cs`, `Services/MetadataSearchService.cs`, `Models/OpenLibrary/OpenLibrarySearchResponse.cs`, `Models/Api/MetadataDtos.cs`, `Controllers/MetadataController.cs`, `Program.cs`, 2 fixtures, the test csproj, and 3 test files.
**Deviations:**
- Extra `MetadataSearchServiceTests.cs` tests `TryNormalizeIsbn` directly (AGENTS.md §5: parsing gets a test), one file over the planned 8: *improvement*.
- Open Library fixtures load through a small local helper because `FakeHttpMessageHandler.Fixture` hardcodes the `audible` folder: *debt*, minor. Give `Fixture` a folder argument the next time a test touches it.
- If Audible fails and Open Library finds nothing, the result is 200 with no candidates and Audible's error in `providerErrors`. The slice didn't specify this; it's consistent with "only all-failed is 502": *fine*.
- `TryNormalizeIsbn` checks the shape, not the check digit: *fine* (a bad ISBN just finds nothing).
- The blank-keywords message now mentions ISBN; the per-provider Warning log moved into the service: *fine*.
**Follow-ups:**
- The search UI should show `providerErrors` beside the results (next slice).
- Open Library candidates leave publisher and language empty; the editor slice decides what print data may fill.
