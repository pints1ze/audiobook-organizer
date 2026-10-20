# Slice 52: Library preview API

**Phase:** 7, Naming & preview
**Goal:** `GET /api/preview` lists every book's files with their destinations and flags duplicate targets and destinations that already exist in the library, and the per-book preview carries the same flags.

## Done when
- [x] `GET /api/preview` returns `{ libraryPath, books: [{ groupId, title, author, files: [...] }], counts: { files, missing, errors, duplicates, existing } }` with every group (in `GetGroups()` order) and its files in book order, each file as in slice 51 plus `duplicate` and `exists` booleans
- [x] `duplicate` is true on every file whose `destinationPath` equals another file's destination anywhere in the preview, compared case-insensitively (in the same book or across books)
- [x] `exists` is true when a file or folder is already at `destinationPath` (`File.Exists || Directory.Exists`); the check only reads the library and nothing is created
- [x] `GET /api/groups/{id}/preview` returns that book's entry with the same flags (so a duplicate against another book shows there too); unknown group → 404
- [x] `counts` count files: `missing` = files with a non-empty `missing`, `errors` = files with an `error`, `duplicates` and `existing` = flagged files
- [x] all four checks pass

## Read first
- `docs/slices/51-book-naming-preview-api.md` and its Outcome (the extra metadata read)
- `src/AudiobookOrganizer.Api/Services/NamingPreviewService.cs`, `Models/Api/PreviewDtos.cs`
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`GetMetadata`, `GetPeople`) and `Data/GroupRepository.cs` (`GetGroups`, `GetGroupFiles`)
- `src/AudiobookOrganizer.Api/Controllers/DashboardController.cs` (a small top-level controller to copy)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupPreviewControllerTests.cs` (setup)

## Tasks
- [x] **DTOs.** `Models/Api/PreviewDtos.cs`: add `bool Duplicate, bool Exists` to `FilePreviewDto`; add `string? Title, string? Author` to `BookPreviewDto` (the group's title and author as `GET /api/groups` shows them); new `PreviewCountsDto(int Files, int Missing, int Errors, int Duplicates, int Existing)` and `LibraryPreviewDto(string? LibraryPath, IReadOnlyList<BookPreviewDto> Books, PreviewCountsDto Counts)`.
- [x] **Service.** `Services/NamingPreviewService.cs`: `LibraryPreviewDto PreviewAll()` builds each group's book preview (the slice 51 logic, settings read once), then flags duplicates by grouping destinations with `StringComparer.OrdinalIgnoreCase` and `exists` by checking the disk, then counts. `Preview(groupId)` returns null for an unknown group, else that book from `PreviewAll()`. Remove the duplicate metadata read from slice 51: `AudiobookService.GetPeople` gets an overload (or a private helper) that takes the already-read metadata.
- [x] **Endpoint.** New `Controllers/PreviewController.cs`: `[Route("api/preview")]`, `[HttpGet]` → 200 with `PreviewAll()`. `GroupsController`'s preview stays.
- [x] **Tests.** New `Tests/Controllers/PreviewControllerTests.cs` and updates to `GroupPreviewControllerTests.cs` (below). Run all four checks, keeping full backend output.
- Data: none (read-only; nothing stored).
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `PreviewControllerTests.cs`: two scanned books under a file template without `%track%` (e.g. `%album%`) → every multi-file book's files are flagged `duplicate`, and `counts.duplicates` matches; with `[%track:2% - ]%album%` none are.
- `PreviewControllerTests.cs`: two books set through `PUT /api/groups/{id}` to titles `The Hobbit` and `the hobbit` with the same author produce destinations that differ only in letter case, and both books' files are flagged `duplicate`.
- `PreviewControllerTests.cs`: a file created in the library at one book's destination path before the GET marks that file `exists` (and only it), and `counts.existing` is 1; the library holds nothing else afterwards.
- `GroupPreviewControllerTests.cs`: the per-book preview shows `duplicate: true` for a file that collides with another book, and the existing tests still pass with the new fields.

## Manual checks
- With the API running, `GET /api/preview` in the browser: every book is listed, counts add up, and a template without `%track%` flags multi-file books as duplicates. Put an empty file at one destination in the library folder and reload: that file shows `exists: true`. Delete the test file afterwards.

## Out of scope
- The preview screen (next slice)
- Resolving conflicts (renaming, skipping, overwriting): processing never overwrites (AGENTS.md) and phase 8 decides how a conflicted book is handled
- Paging or filtering the preview; one user's library is small enough for one response (shortcut, revisit if it's slow on the real collection)

## Notes
- File count: 6 (`PreviewDtos.cs`, `NamingPreviewService.cs`, `AudiobookService.cs`, new `PreviewController.cs`, new `PreviewControllerTests.cs`, `GroupPreviewControllerTests.cs`). Under the cap; `Program.cs` doesn't change.
- Why next: slice 51's follow-up, and the rest of phase 7's done-when (duplicate targets and existing-file conflicts) needs a library-wide view.
- Duplicates are compared case-insensitively on every OS: dev is Windows, and the library may sit on a case-insensitive share that Audiobookshelf reads, so `Book` and `book` are treated as the same target. Record this as a slice decision in the review.
- Files whose destination is null (an error) are never duplicates or existing.
- Unresolved and excluded files aren't in any group, so they're never previewed (keeps "excluded files never get an `Audiobook`").
- The per-book endpoint now builds the whole preview to get cross-book flags: O(all books) per request, fine for one user (shortcut, same revisit condition as paging).
- Conflict check: read-only, consistent with "never overwrite an existing destination file" (phase 8 enforces it). No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 337 passed, 0 failed | Was 331; `--filter PreviewController\|GroupPreview` gives 10 passed. Full output kept (TRX + console) |
| Frontend tests | 239 passed, 0 failed | Unchanged (backend-only slice) |
| New tests | 4 of 4 present, passing | `PreviewControllerTests.cs` (5): template without `%track%` flags duplicates / with it none; case-only title difference flagged across books; an existing library file flags only that file and `counts.existing` is 1; counts for missing and errors, error files never flagged. `GroupPreviewControllerTests.cs`: cross-book duplicate on the per-book preview; existing tests assert the new flags false |
| Manual: GET `/api/preview`, counts, duplicates, an existing file | Not run | API not running. Start with `scripts/start-api.ps1` |

Data checks: none (read-only).

## Outcome (2026-10-19, commit 4a60c8d)

**Status:** Implemented
**Summary:** `GET /api/preview` lists every group's files with destinations, flags destinations shared by two or more files (ignoring case, across books) as `duplicate` and destinations already on disk as `exists`, and returns file counts for missing, errors, duplicates and existing. The per-book preview is that book's entry, so it carries the same flags. `AudiobookService.GetPeople(metadata)` removes slice 51's extra metadata read.
**Changed:** 6 files: `PreviewDtos.cs` (flags, title/author, counts, `LibraryPreviewDto`), `NamingPreviewService.cs` (`PreviewAll`, `Preview` from it), `AudiobookService.cs` (`GetPeople` overload), new `PreviewController.cs`, new `PreviewControllerTests.cs` (5), `GroupPreviewControllerTests.cs` (+1).
**Deviations:**
- Each book also carries `libraryPath` (as in slice 51) besides the top level: *fine*.
- One extra test for missing/error counts and error files never being flagged: *improvement*.
- A book's `title`/`author` is the group's (as Incoming shows), not the metadata title; the destination uses the metadata title, so the two can differ after a match: *fine* for the API. The preview screen should label books by the rendered metadata title or show both; decide in the next slice.
- `Preview(groupId)` builds the full preview (marked `// shortcut:`), as the slice allowed: *fine*.
**Follow-ups:**
- Next: the preview screen (`/preview`): counts, each book with source → destination per file, and the missing, error, duplicate and exists flags.
- Manual check not run (API down); carried to the phase 7 verification.
