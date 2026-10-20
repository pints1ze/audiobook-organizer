# Slice 51: Book naming preview API

**Phase:** 7, Naming & preview
**Goal:** `GET /api/groups/{id}/preview` maps one book's metadata and canonical names to template tokens and shows, for each of its files in order, the source and the destination path the saved templates produce, with missing tokens and errors.

## Done when
- [x] tokens map as the developer decided (Notes): `artist` = first author, `narrator` = first narrator, each as its canonical name when linked, else the stored name; `album` and `title` = the book title; `series` and `series-part` from metadata; `year` = the release date's leading 4 digits; `track` = the file's 1-based position in book order (a single-file book gets 1); `disc` = the file's scanned disc tag
- [x] `GET /api/groups/{id}/preview` returns `{ groupId, libraryPath, files: [{ fileId, sourcePath, relativePath, destinationPath, missing, error }] }` in book order: `relativePath` is the rendered directory template + `/` + rendered file template + the source extension (lower case); `destinationPath` is `NamingTemplate.ResolveUnder(libraryPath, relativePath)`
- [x] `missing` lists the union of required tokens without a value from both templates; a render error, an unset library path ("The library folder isn't set.") or a path `ResolveUnder` refuses ("The path would land outside the library.") sets `error` with null paths. Unknown group → 404
- [x] nothing is written or created on disk
- [x] all four checks pass

## Read first
- `docs/CURRENT-STATE.md` (slice 44 and 45 decisions, the 2026-10-19 track decision) and `docs/PRD.md` naming lines
- `src/AudiobookOrganizer.Api/Services/Naming/NamingTemplate.cs` (`Tokens`, `Render`, `ResolveUnder`) and `NamingModels.cs`
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`GetMetadata`, `GetPeople`) and `Services/SettingsService.cs` (`Get`, the default templates)
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`GetGroupFiles`, `GroupFileRow`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupPeopleControllerTests.cs` (scan + settings setup) and `Services/NamingTemplateTests.cs`

## Tasks
- [x] **Token mapping (pure).** New `Services/Naming/BookTokens.cs`: `public static IReadOnlyDictionary<string, string?> For(BookMetadataFieldsDto fields, string? artist, string? narrator, int position, int? disc)` returning a value for every name in `NamingTemplate.Tokens` as above (`year` null unless the release date starts with 4 digits; `track` = `position.ToString()`). `artist`/`narrator` are passed in already resolved, so the mapper stays pure.
- [x] **Preview service.** New `Services/NamingPreviewService.cs` (singleton in `Program.cs`) taking `AudiobookService`, `GroupRepository` and `SettingsService`. `BookPreviewDto? Preview(long groupId)`: null for an unknown group; reads metadata and `GetPeople` once (first author's and narrator's `entry?.Name ?? name`), the files from `GetGroupFiles` in order, and the settings' templates and library path. For each file: render both templates with `BookTokens.For(…, position, file.Disc)`, merge `missing` (no duplicates, template order), join, append the extension, resolve. Both templates come from `SettingsService.Get()` (defaults when unset).
- [x] **DTOs and endpoint.** New `Models/Api/PreviewDtos.cs`: `FilePreviewDto(long FileId, string SourcePath, string? RelativePath, string? DestinationPath, IReadOnlyList<string> Missing, string? Error)` and `BookPreviewDto(long GroupId, string? LibraryPath, IReadOnlyList<FilePreviewDto> Files)`. `Controllers/GroupsController.cs`: `[HttpGet("{id:long}/preview")]` → 200 or 404.
- [x] **Tests.** New `Tests/Services/BookTokensTests.cs` and `Tests/Controllers/GroupPreviewControllerTests.cs` (below). Run all four checks, keeping full backend output (TRX + console) per the slice 49 decision.
- Data: none (read-only; nothing stored).
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `BookTokensTests.cs`: a book with two authors and two narrators maps `artist`/`narrator` to the first passed-in names, `album` and `title` to the title, `year` `2021` from `2021-05-04`, `track` `3` for position 3, `disc` from the argument; a release date of `unknown` gives a null `year`; every name in `NamingTemplate.Tokens` is a key.
- `GroupPreviewControllerTests.cs`: unknown group → 404; with settings templates `%artist%/%album%` and `[%track:2% - ]%title%`, a scanned two-file book whose author `Author, Ann` is an alias of person `Ann Author` previews `Ann Author/<title>/01 - <title>.mp3` and `.../02 - <title>.mp3` in book order, with `destinationPath` under the library path.
- `GroupPreviewControllerTests.cs`: a template using `%series%` (required) on a book with no series lists `series` in `missing` for every file; a reorder through `PUT /api/groups/{id}/order` swaps the track numbers in the next preview.
- `GroupPreviewControllerTests.cs`: with no library path saved, each file has the "library folder isn't set" error and null paths.

## Manual checks
- With the API running and a book matched, `GET /api/groups/{id}/preview` in the browser: paths follow the Settings templates and use canonical author names; change the directory template on `/settings` and the next GET follows it. Confirm nothing appears in the library folder.

## Out of scope
- The library-wide preview with duplicate targets and existing-file conflicts (next slice), and the preview screen (after that)
- Writing track tags (phase 8 writes each file's position as its track tag; noted there)
- A `%genre%` token (not in the PRD's token list)

## Notes
- Developer decisions 2026-10-19 (asked while writing this slice): `%artist%`/`%narrator%` use the first author/narrator only; `%title%` is always the book title; `%track%` is the file's position in book order (one-file books get 1), and processing will write that position as the file's track tag.
- File count: 7 (`BookTokens.cs`, `NamingPreviewService.cs`, `PreviewDtos.cs`, `GroupsController.cs`, `Program.cs`, `BookTokensTests.cs`, `GroupPreviewControllerTests.cs`). Under the cap.
- Why next: slice 50 finished linking; PLAN.md's phase 7 order is book-to-token mapping, then the preview. Mapping alone has no observable result, so this slice pairs it with a per-book preview endpoint.
- A library path that's unset in settings: `SettingsService.Get()` returns it as null; the check is on the service side, not a new validation rule.
- `ResolveUnder` gets the library path as stored; it's validated on save (slice 05), so the preview doesn't re-check that it exists.
- Conflict check: keeps "scans are read-only" and the template decisions of slices 44–45 (sanitizing, `/` in a value becomes `-`). No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 331 passed, 0 failed | Was 324; `--filter BookTokens\|GroupPreview` gives 7 passed. Full output kept (TRX + console) |
| Frontend tests | 239 passed, 0 failed | Unchanged (backend-only slice) |
| New tests | 4 of 4 present, passing | `BookTokensTests.cs` (3): first author/narrator, title, year, track, disc; non-numeric date → null year; every token is a key. `GroupPreviewControllerTests.cs` (4): 404; canonical author and padded tracks in book order under the library; missing `series` and reorder swapping tracks; unset library path error with null paths |
| Manual: GET `/api/groups/{id}/preview`, change a template, confirm the library folder is untouched | Not run | API not running. Start with `scripts/start-api.ps1` |

Data checks: none (read-only).

## Outcome (2026-10-19, commit e6557c9)

**Status:** Implemented
**Summary:** `BookTokens.For` maps a book's metadata, its first author and narrator (canonical when linked), its title, year, series, and each file's position and disc to the naming tokens. `NamingPreviewService.Preview` renders the saved directory and file templates per file in book order and returns relative and resolved destination paths with missing tokens and errors through `GET /api/groups/{id}/preview`. Nothing is written.
**Changed:** 7 files: new `Services/Naming/BookTokens.cs`, `Services/NamingPreviewService.cs`, `Models/Api/PreviewDtos.cs`, `BookTokensTests.cs` (3), `GroupPreviewControllerTests.cs` (4); `GroupsController.cs` (endpoint), `Program.cs` (registration).
**Deviations:**
- The unset-library test deletes the `LibraryPath` row directly, because settings validation won't save a blank one: *fine*.
- A whitespace-only stored library path counts as unset, and `missing` is still filled when `error` is set: *improvement*.
- `GetPeople` re-reads the metadata that `Preview` already read (one extra read per book): *debt*, small. Worth sharing when the library-wide preview loops over every book.
- The "outside the library" error has no integration test (sanitizing makes it hard to trigger; `ResolveUnder` is unit-tested): *fine*.
**Follow-ups:**
- Next: the library-wide preview: every book not excluded, with duplicate targets (two files resolving to the same destination, case-insensitively) and existing-file conflicts (a destination that already exists) flagged. Then the preview screen.
- Manual check not run (API down); carried to the phase 7 verification.
