# Slice 34: Embedded cover API

**Phase:** 5, Metadata
**Goal:** the API lists the embedded covers in a book's files, serves each as an image, and stores the one the user picks as the book's chosen cover.

## Done when
- [x] `GET /api/groups/{id}/cover` returns `{ groupId, chosen, embedded }`. `chosen` is `{ source, fileId, mimeType, chosenAtUtc }` or null. `embedded` lists `{ fileId, relativePath }` for each of the group's files whose scan found a cover (`AudioFile.HasCover`), in file order. Unknown group → 404.
- [x] `GET /api/groups/{id}/files/{fileId}/cover` returns the file's first embedded picture as bytes with its content type (`image/jpeg` or `image/png`). It reads the file with TagLibSharp and never saves it. 404 when the group is unknown, the file isn't in the group, or the file has no picture.
- [x] `PUT /api/groups/{id}/cover` with `{ source: "embedded", fileId }` copies that picture into the database as the book's chosen cover, creates the `Audiobook` if missing, marks the group `UserCorrected`, and returns the same body as the GET. Picking again replaces the earlier choice. 400 (`fileId`) "This file isn't in the book." or "This file has no embedded cover."; 400 (`fileId`) "The cover must be a JPEG or PNG." for another type; 400 (`fileId`) "The cover can be at most 10 MB."; 400 (`source`) "Choose a cover source." for any other source; 404 for an unknown group.
- [x] `GET /api/groups/{id}/cover/image` returns the chosen cover's bytes with its content type, or 404 when there's none.
- [x] All four checks pass.

## Read first
- `docs/CURRENT-STATE.md`, Decisions (2026-10-18 covers; slice 13 `UserCorrected`; slice 30 one current match)
- `src/AudiobookOrganizer.Api/Services/AudioFileReader.cs` (how TagLibSharp is opened read-only; `tag.Pictures`)
- `src/AudiobookOrganizer.Api/Data/AudiobookRepository.cs` (`SaveFields`: creating the `Audiobook` and setting `UserCorrected` in one transaction), `Data/DatabaseInitializer.cs`, `Data/GroupRepository.cs` (how a group's files are read)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (result records, error dictionaries) and `Controllers/GroupsController.cs` (`Invalid`, 404 handling)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupMatchControllerTests.cs` and `ScanTestFolders` (scan helpers, temp folders)

## Tasks
- [x] **Schema and repository.** In `DatabaseInitializer.cs` add `AudiobookCover (AudiobookId INTEGER PRIMARY KEY REFERENCES Audiobook (Id) ON DELETE CASCADE, Source TEXT NOT NULL, FileId INTEGER, MimeType TEXT NOT NULL, Image BLOB NOT NULL, ChosenAtUtc TEXT NOT NULL)`. Add an `AudiobookCover` entity in `Models/Local/Audiobook.cs`. In `AudiobookRepository.cs` add `GetCover(groupId)` (without the image), `GetCoverImage(groupId)`, `SaveCover(groupId, cover, nowUtc)` (upsert, create the `Audiobook` if missing, set `UserCorrected = 1`, one transaction) and `GetCoverFiles(groupId)` (the group's files with `HasCover = 1`, in position order, with their paths). Verify: the tests below; `DatabaseInitializerTests` still pass.
- [x] **Cover service.** New `Services/CoverService.cs` with `GetCovers(groupId)`, `ReadEmbedded(groupId, fileId)` (the first picture through TagLibSharp, read-only; `null` when the file isn't in the group, is missing, or has no picture) and `ChooseCover(groupId, request)` returning a result record with the cover or errors, as `AudiobookService` does. Check the type (JPEG or PNG, from the picture's MIME type) and the 10 MB limit before saving. Register it in `Program.cs`. Verify: `GroupCoverControllerTests`.
- [x] **DTOs and endpoints.** In `Models/Api/MetadataDtos.cs` add `ChosenCoverDto(string Source, long? FileId, string MimeType, DateTimeOffset ChosenAtUtc)`, `EmbeddedCoverDto(long FileId, string RelativePath)`, `BookCoverDto(long GroupId, ChosenCoverDto? Chosen, IReadOnlyList<EmbeddedCoverDto> Embedded)` and `ChooseCoverDto(string? Source, long? FileId)`. In `GroupsController.cs` add the four routes from Done when, returning images with `File(bytes, mimeType)`. Verify: `GroupCoverControllerTests`.
- Data: new `AudiobookCover` table via `CREATE TABLE IF NOT EXISTS` (existing databases get it at startup), one row per `Audiobook`, cascade-deleted with it. The image is a BLOB copied from the file, so the choice survives the source being moved and needs no extra folder under `/data`.
- Auth: none (single user, no login). File reads use only paths already recorded by the scan for files in the group, so no request can name an arbitrary path.
- Accessibility: none (no UI).

## Tests to add
All in a new `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs`. Set up by copying `docs/fixtures/audio/chapters.mp3` into the temp incoming folder and adding a small PNG (and, for one test, a JPEG) with TagLibSharp on that temp copy. MP3 only: never save an M4B with TagLibSharp.
- `GET /cover` for a scanned book lists the file with a cover under `embedded` and not the one without, with `chosen: null`. An unknown group gets 404.
- `GET /files/{fileId}/cover` returns the PNG's exact bytes as `image/png`. A file with no picture, and a file from another group, get 404. The source file's bytes and mtime are unchanged afterwards.
- `PUT /cover` with the embedded source stores the cover: the GET shows `chosen` with `source: "embedded"`, the file id and `image/png`, `/cover/image` returns the same bytes, and the group reads `userCorrected: true`. Picking the JPEG file then replaces it.
- `PUT /cover` returns 400 for a file with no cover, a file outside the book, and an unknown source, and stores nothing each time. `/cover/image` is 404 before any pick.

## Manual checks
- Run `scripts/start-api.ps1`, scan a real incoming folder that has a book with embedded art, and open `/api/groups/{id}/cover` and `/api/groups/{id}/files/{fileId}/cover` in the browser: the image shows.
- `PUT` the embedded cover (e.g. `Invoke-RestMethod -Method Put -ContentType application/json -Body '{"source":"embedded","fileId":N}'`), then open `/api/groups/{id}/cover/image`. Rescan and confirm the cover is still chosen.

## Out of scope
- Downloading the match's cover (next slice, 35) and uploading an image (36)
- The cover picker on the book page (after 35 and 36)
- Embedding the cover into output files (phase 8)
- Resizing, cropping or converting images; covers other than the first picture in a file
- Cleaning up covers for groups the rules regroup away (choosing a cover marks the group `UserCorrected`, so regrouping keeps it)

## Notes
- File count: 8 (`DatabaseInitializer.cs`, `Audiobook.cs`, `AudiobookRepository.cs`, new `CoverService.cs`, `Program.cs`, `MetadataDtos.cs`, `GroupsController.cs`, new `GroupCoverControllerTests.cs`).
- Split: phase 5's cover line (embedded, download, upload) is three slices plus the UI. This one sets up storage and the embedded source; download (35) and upload (36) add sources to the same `PUT` and table, and the book page picker follows.
- Storage choice: the chosen image lives in SQLite as a BLOB, not as a file under `/data`. It's one row per book, deleted with the book, written in the same transaction as the choice, and test databases stay isolated with no shared covers folder. Mark it `// shortcut: covers stored as BLOBs — revisit if the database grows past a few hundred MB` in `DatabaseInitializer`.
- The scan's `HasCover` flag decides what's listed; `ReadEmbedded` still checks the file, since it may have changed since the scan.
- Choosing a cover counts as a user correction (`UserCorrected = 1`), following decision 2026-10-19 (slice 30), so rescans keep it.
- Conflict check: no new package (TagLibSharp is already in). Scans stay read-only; the cover endpoints only read source files. Excluded files have no group, so they can't supply a cover.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 174 passed, 0 failed | Was 169. Rerun after the fix commit `2c506d7` |
| Frontend tests | 175 passed, 0 failed | Unchanged (no client change) |
| New tests | 4 of 4 present, passing, plus 1 fix test | `GroupCoverControllerTests`: list + 404, exact bytes + source unchanged, pick + replace + `userCorrected`, 400s/404 storing nothing (also GIF, over-10 MB, missing `fileId`/`source`). Fix test `Get_file_cover_serves_only_jpeg_or_png_whatever_type_the_tag_claims` failed before the fix (200 for `text/html`), passes after |
| Manual: real folder, cover URLs show the image | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |
| Manual: PUT embedded cover, rescan keeps it | Not run | Same |

Data checks for the developer (not run): `SELECT AudiobookId, Source, FileId, MimeType, length(Image), ChosenAtUtc FROM AudiobookCover;`

## Outcome (2026-10-19, commit 5553106, fix 2c506d7)

**Status:** Implemented
**Summary:** Books can list the embedded covers in their files, serve each one, and store a chosen cover as a BLOB in the new `AudiobookCover` table (`GET`/`PUT /api/groups/{id}/cover`, `GET .../cover/image`, `GET .../files/{fileId}/cover`). Choosing creates the `Audiobook` and marks the group `UserCorrected`. The file-cover GET serves only JPEG or PNG, after a fix commit.
**Changed:** 8 files: new `Services/CoverService.cs`, `Data/AudiobookRepository.cs` (`GetCover`, `GetCoverImage`, `SaveCover`, `GetCoverFiles`), `Data/DatabaseInitializer.cs`, `Models/Local/Audiobook.cs`, `Models/Api/MetadataDtos.cs`, `Controllers/GroupsController.cs`, `Program.cs`, new `Tests/Controllers/GroupCoverControllerTests.cs`.
**Deviations:**
- `GET /api/groups/{id}/files/{fileId}/cover` returns whatever MIME type the file's tag records (or `application/octet-stream`), not only `image/jpeg`/`image/png` as Done when says. A tag can claim `text/html` or `image/svg+xml`, and the browser would then run the "image" as a page on the app's origin: *bug*, fixed with the developer's OK in `2c506d7`: `ReadEmbedded` applies the same `ImageType` check as `ChooseCover`, so other types get 404 and `image/jpg` is served as `image/jpeg`. Test added and seen to fail first.
- `CoverService` reads relative paths through `GroupingService.GetDetail` and adds `GetChosenImage`: *fine*.
- MIME matching is case-insensitive and `image/jpg` is stored as `image/jpeg`: *improvement*.
- A missing, corrupt or unsupported file counts as "no picture" (404 / "This file has no embedded cover."): *fine*.
- Extra tests for GIF, over-10 MB, and missing `fileId`/`source`: *improvement*.
**Follow-ups:**
- Slice 35: download the match's cover as a second `PUT /cover` source. Slice 36: upload. Then the book page's cover picker.
