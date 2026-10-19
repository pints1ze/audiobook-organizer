# Slice 27: Audible search

**Phase:** 5, Metadata
**Goal:** `GET /api/metadata/search?keywords=...` asks Audible's catalog and returns normalized book candidates through a shared provider interface.

## Done when
- [x] `GET /api/metadata/search?keywords=project%20hail%20mary` returns 200 with a list of candidates. Each has provider `audible`, ASIN, title, subtitle, authors, narrators, series (name and part), publisher, release date, language, runtime in minutes, format (`unabridged`/`abridged`) and a cover URL.
- [x] Blank or missing `keywords` returns a 400 validation problem with no provider call.
- [x] An Audible failure (non-success status, timeout or unreadable JSON) returns a 502 problem whose message names the provider. Nothing is thrown unhandled, and the failure is logged.
- [x] Real Audible responses are saved under `docs/fixtures/audible/`, and every test uses them through a fake `HttpMessageHandler`. No test reaches the network.
- [x] All four checks pass.

## Read first
- `AGENTS.md` (Stack: metadata providers; Layout: `Models/<Ext>/` matches the external JSON exactly; one client per external system)
- `docs/PRD.md`, Identify workflow, Constraints (the Audible URL and its parameters), Rules ("A provider failure never blocks manual editing")
- `src/AudiobookOrganizer.Api/Program.cs` (DI registration style)
- `src/AudiobookOrganizer.Api/Controllers/GroupsController.cs` (controller and problem style)
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizerWebApplicationFactory.cs` and `AudiobookOrganizer.Api.Tests.csproj` (how fixtures are copied to the test output)

## Tasks
- [x] **Capture fixtures.** Save two real responses from `https://api.audible.com/1.0/catalog/products?response_groups=contributors,media,product_desc,product_attrs,product_extended_attrs,series&image_sizes=500&num_results=5&products_sort_by=Relevance&keywords=...`, unedited: `search-project-hail-mary.json` (standalone book, `series` is null) and `search-dungeon-crawler-carl.json` (series with a `sequence`). Add `docs\fixtures\audible\*.json` to the test csproj `Content` items, copied the same way as the audio fixtures. Verify: both files parse, and the second has a non-null `series`.
- [x] **External shapes.** Add `Models/Audible/AudibleCatalogResponse.cs` with only the fields used: `products[]` holding `asin`, `title`, `subtitle`, `authors[{asin,name}]`, `narrators[{name}]`, `series[{asin,title,sequence}]`, `publisher_name`, `release_date`, `language`, `runtime_length_min`, `format_type` and `product_images` (a dictionary keyed by size). Property names map to the JSON with `[JsonPropertyName]`. Verify: it deserializes both fixtures.
- [x] **Provider interface and candidate.** Add `Services/IMetadataProvider.cs` (`string Name`, `Task<IReadOnlyList<MetadataCandidateDto>> SearchAsync(string keywords, CancellationToken ct)`) and `Models/Api/MetadataDtos.cs` (`MetadataCandidateDto`, `SeriesRefDto`). Series part stays a string, because Audible sends values like `"1"`, `"1.5"` or empty, and parsing waits for the editor slice. Throw `MetadataProviderException` (in the same file as the interface) for any provider failure.
- [x] **Audible client.** Add `Services/AudibleClient.cs` as a typed `HttpClient` registered with `AddHttpClient<AudibleClient>()` in `Program.cs`: base address `https://api.audible.com/`, a 15 s timeout, and the query string from the PRD with `keywords` URL-encoded. It maps products to candidates: trim strings, drop empty contributor names, use the `500` image (otherwise the largest). It wraps `HttpRequestException`, timeouts and `JsonException` in `MetadataProviderException`. Register it as `IMetadataProvider` too. Verify: unit tests against the fixtures.
- [x] **Endpoint.** Add `Controllers/MetadataController.cs` with `GET api/metadata/search?keywords=`. Blank keywords → `ValidationProblem` (field `keywords`). `MetadataProviderException` → `Problem(statusCode: 502, detail: "Audible search failed: ...")`, logged at Warning. Otherwise 200 with the candidates. Verify: integration tests that swap the handler through `ConfigureTestServices`.
- Data: none. Search results aren't stored yet; `MetadataMatch` comes with the pick-a-match slice.
- Auth: none (single user, no login).
- Accessibility: none (no UI in this slice).

## Tests to add
- `Api.Tests/Services/AudibleClientTests.cs`: the Project Hail Mary fixture maps to a candidate with ASIN `B08G9PRS1K`, author Andy Weir, narrator Ray Porter, runtime 970, `unabridged`, a 500px cover URL and no series.
- `AudibleClientTests`: the Dungeon Crawler Carl fixture maps series name and part from `series[0]`.
- `AudibleClientTests`: the request URL carries the PRD's response groups and the encoded keywords (captured by the fake handler).
- `AudibleClientTests`: a 503 and a malformed body both throw `MetadataProviderException`.
- `Api.Tests/Controllers/MetadataControllerTests.cs`: blank keywords → 400 with no handler call; a fixture-backed search → 200 with candidates; a failing handler → 502 problem.

## Manual checks
- Run `scripts/start-api.ps1` and open `http://localhost:5070/api/metadata/search?keywords=project%20hail%20mary`. You should get real candidates back.
- Disconnect the network (or point the hosts file at an unreachable address) and repeat. You should get a 502 problem, the API keeps running, and the log has one Warning.

## Out of scope
- Open Library fallback (next slice, same interface)
- Search UI on the Incoming page, picking a match, storing `MetadataMatch`
- The canonical `Audiobook` record, value origins, the editor, covers
- Throttling and caching: searches are user-triggered one at a time, and the rate-limit question stays open
- Marketplaces other than `.com`

## Notes
- File count: 10, of which 3 are trivial (2 fixture JSON files, one csproj line). The 7 code files are within the cap.
- Conflict check: `HttpClient` and `IHttpClientFactory` ship with ASP.NET Core and `System.Text.Json` with .NET, so there's no new package. The layout puts clients in `Services/` and shapes in `Models/Audible/`. The PRD fixes the URL and parameters. A sample call on 2026-10-19 returned 200 with the field names above (`authors`, `narrators`, `publisher_name`, `release_date`, `runtime_length_min`, `product_images`, `format_type`).
- The open question on rate limits doesn't block this slice: there are no automatic or bulk calls. Any throttling decision belongs with the pick-a-match slice.
- Remaining phase 5 pieces, in rough order: Open Library fallback provider; search UI and picking a candidate (`Audiobook` + `MetadataMatch` tables); a metadata editor with current vs proposed values and their origin, keeping user edits across re-searches; manual completion with no match; cover from embedded, download or upload. Excluded files (`AudioFile.UserState`) never get an `Audiobook`.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors. Run with `--artifacts-path` in the scratchpad because the running dev API locks `bin/`. |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 132 passed, 0 failed | Was 117; 15 new |
| Frontend tests | 129 passed, 0 failed | 9 files |
| New tests | 5 of 5 present, passing | `AudibleClientTests` (10 cases incl. trimming, image fallback, timeout) and `MetadataControllerTests` (5 cases) |
| Manual: live search | Passed | New build run on port 5071 with a scratch DB (the dev API on 5070 predates the slice). `keywords=project hail mary` → 200, 50 candidates, first is B08G9PRS1K, Andy Weir / Ray Porter, 970 min, unabridged, 500px cover. |
| Manual: blank keywords | Passed | `keywords=%20` → 400, `keywords`: "Enter a title, author or ASIN to search for." |
| Manual: network down → 502 | Not run | Can't drop the network from this session. Covered by `Provider_failure_returns_502_problem_naming_the_provider` and the timeout and 503 client tests. |

## Outcome (2026-10-19, commit 366c3e6)

**Status:** Implemented
**Summary:** `GET /api/metadata/search?keywords=` searches Audible's catalog through `AudibleClient` (a typed `HttpClient` behind `IMetadataProvider`). It returns `MetadataCandidateDto`s, gives 400 for blank keywords and 502 for any provider failure. All Done when items are met, and a live search returned 50 real candidates.
**Changed:** 12 files: `Services/AudibleClient.cs`, `Services/IMetadataProvider.cs`, `Controllers/MetadataController.cs`, `Models/Audible/AudibleCatalogResponse.cs`, `Models/Api/MetadataDtos.cs`, `Program.cs`, 2 fixtures under `docs/fixtures/audible/`, the test csproj, and 3 test files.
**Deviations:**
- `format` passes the provider's string through (real products include `original_recording`), and `runtimeMinutes` is nullable: *improvement*, keep.
- The Project Hail Mary fixture leaves out `series` instead of sending `null`; it maps the same: *fine*.
- Extra test helper `Api.Tests/Services/FakeHttpMessageHandler.cs`, reusable by the Open Library slice: *fine*.
- The client asks for `num_results=50` per the PRD; fixtures were captured with 5: *fine*.
- Build and tests ran with `--artifacts-path` because the running dev API locks `bin/`; the code is unaffected: *fine*. Stop the dev API before running the plain commands.
**Follow-ups:**
- The Open Library fallback reuses `IMetadataProvider` and `FakeHttpMessageHandler`. With two providers, the controller will need to pick or chain them instead of taking a single `IMetadataProvider` (next slice).
- Parsing the series part (`"1.5"`, empty) waits for the editor slice.
