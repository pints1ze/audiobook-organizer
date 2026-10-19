# Slice 35: Provider cover download

**Phase:** 5, Metadata
**Goal:** the user can choose the chosen match's cover; the API downloads it from the catalog and stores it as the book's cover.

## Done when
- [x] `GET /api/groups/{id}/cover` also returns `provider`: `{ provider, coverUrl }` from the book's current match when the match has a cover URL, else null. `chosen` and `embedded` are unchanged.
- [x] `PUT /api/groups/{id}/cover` with `{ source: "provider" }` downloads the current match's `coverUrl` and stores it like an embedded pick (`source: "provider"`, `fileId: null`), creating the `Audiobook` if missing and marking the group `UserCorrected`. It returns the GET body. The URL always comes from the stored match, never from the request.
- [x] 400 (`source`) "Choose a match with a cover first." when the book has no match or the match has no cover URL; 400 (`source`) "The cover URL isn't from a known catalog." unless the URL is `https` on `m.media-amazon.com` or `covers.openlibrary.org`; 400 (`source`) "The cover must be a JPEG or PNG." when the downloaded bytes don't start with a JPEG (`FF D8 FF`) or PNG (`89 50 4E 47 0D 0A 1A 0A`) signature, whatever the response's content type; 400 (`source`) "The cover can be at most 10 MB." Nothing is stored on any error.
- [x] A failed download (non-2xx status, timeout, network error) returns 502 with detail "The cover couldn't be downloaded. Try again later." and stores nothing. The embedded source from slice 34 still works.
- [x] All four checks pass.

## Read first
- `docs/slices/34-embedded-cover-api.md`, `## Outcome` (the fix: only JPEG and PNG are served)
- `src/AudiobookOrganizer.Api/Services/CoverService.cs` (`ChooseCover`, `ImageType`, `MaxCoverBytes`)
- `src/AudiobookOrganizer.Api/Services/OpenLibraryClient.cs` and its registration in `Program.cs` (typed `HttpClient`, timeout, User-Agent)
- `src/AudiobookOrganizer.Api/Services/IMetadataProvider.cs` (`MetadataProviderException`) and `Controllers/MetadataController.cs` (the 502 problem)
- `src/AudiobookOrganizer.Api/Data/AudiobookRepository.cs` (`GetMatch`) and `AudiobookService.cs` (how `CandidateJson` is read back)
- `src/AudiobookOrganizer.Api.Tests/Services/FakeHttpMessageHandler.cs` and `Controllers/MetadataControllerTests.cs` (`ConfigureTestServices` swapping a typed client's handler)

## Tasks
- [x] **Download client.** New `Services/CoverDownloadClient.cs`: a typed `HttpClient` (15 s timeout, the same User-Agent as Open Library) with `DownloadAsync(Uri url, CancellationToken)` returning the bytes, or throwing `CoverDownloadException` on a non-2xx status, timeout or network error. It reads the body with `HttpCompletionOption.ResponseHeadersRead` and stops after `MaxCoverBytes + 1` bytes, so a huge response is never held whole. Register it in `Program.cs`. Verify: `CoverDownloadClientTests`.
- [x] **Provider source.** In `CoverService.cs` make the choice async (`ChooseCoverAsync`). Add the `"provider"` source: read the current match's `coverUrl`, check the host allow-list, download, detect the type from the bytes' signature, check the size, save with `FileId = null`. Add the `provider` option to `GetCovers`. In `Models/Api/MetadataDtos.cs` add `ProviderCoverDto(string Provider, string CoverUrl)` to `BookCoverDto`. In `GroupsController.cs` await the PUT and turn `CoverDownloadException` into the 502 problem. Verify: `GroupCoverControllerTests`.
- Data: none (`AudiobookCover` from slice 34 already allows `FileId` null and any `Source`).
- Auth: none (single user, no login). The download URL is the stored match's, limited to `https` on two catalog image hosts, so a request can't make the server fetch an arbitrary or LAN address.
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Services/CoverDownloadClientTests.cs`: returns the bytes on 200; throws `CoverDownloadException` on a 404 and on a timeout (`TaskCanceledException` from the handler); stops reading a response that's over 10 MB.
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs`: after picking an Audible candidate with a `m.media-amazon.com` cover URL, `GET /cover` shows the `provider` option, and `PUT { source: "provider" }` stores the fake handler's JPEG: `chosen.source` is `"provider"`, `fileId` null, `/cover/image` returns those bytes, and exactly one request went to that URL.
- Same file: 400 with no match, with a match whose cover URL is on another host (e.g. `https://192.168.1.10/x.jpg`, and no request is made), and for HTML bytes served as `image/jpeg`; 502 when the handler returns 503. `AudiobookCover` stays empty each time.

## Manual checks
- Run `scripts/start-api.ps1`, pick an Audible match for a book from the book page, then `PUT /api/groups/{id}/cover` with `{"source":"provider"}` and open `/api/groups/{id}/cover/image`: the catalog cover shows.
- Pick an Open Library match that has a cover and repeat. Open Library's cover URLs redirect to `archive.org`; confirm the download still works.

## Out of scope
- Uploading an image (next slice, 36), and the book page's cover picker (after 36)
- Downloading a cover from any search result other than the chosen match
- Recording which provider a downloaded cover came from (the match already records its provider), retries or caching
- Resizing or converting images

## Notes
- File count: 7 (new `CoverDownloadClient.cs`, `CoverService.cs`, `Program.cs`, `MetadataDtos.cs`, `GroupsController.cs`, new `CoverDownloadClientTests.cs`, `GroupCoverControllerTests.cs`).
- The client's candidate is validated but not re-fetched when a match is picked (decision, slice 30), so a stored `coverUrl` can be any absolute http(s) URL. That's why the host allow-list sits here, at download time. The hosts come from the fixtures (`docs/fixtures/audible/`: `m.media-amazon.com`) and `OpenLibraryClient` (`covers.openlibrary.org`). Keep them in one constant list in `CoverService`; no config option for one use.
- Redirects: `HttpClient` follows them by default and refuses `https` → `http`. Open Library needs that (it redirects to `archive.org`), so the allow-list checks the first URL only. Mark this with a `// shortcut:` comment, revisit if another catalog is added.
- The type comes from the bytes, not the response header, because CDNs sometimes send `binary/octet-stream`.
- One client per external system (AGENTS.md): the image CDNs are their own system, separate from the catalog search clients.
- Conflict check: no new package. Audible's terms and rate limits are still an open question; one cover per explicit pick adds very little load.

## Test results (2026-10-19)

**Regressions:** none. An intermittent frontend failure was traced to a bug in `UnresolvedFiles` and fixed in `8345381` (below).

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 182 passed, 0 failed | Was 174 |
| Frontend tests | 176 passed, 0 failed | First run: 1 failure in `IncomingPage.test.tsx`. Shuffled runs pinned it to "reloads, drops the moved file and focuses the status message after a move". Cause: `UnresolvedFiles` reset its selection in a `[files]` effect that runs after paint, so a box ticked before it ran was cleared (confirmed with temporary logging). Fixed with the developer's OK in `8345381`: the reset happens during render. A new `UnresolvedFiles` test ticks a box before effects run; it failed 5 of 5 before the fix and passes after. After the fix: 0 failures in 80 runs of the move test and 25 shuffled runs of the file. All four checks rerun after the fix |
| New tests | 3 of 3 present, passing | `CoverDownloadClientTests` (4: 200 bytes, 404, timeout, over-10-MB cut-off); `GroupCoverControllerTests` (+4: provider option and download stores the JPEG with one request, 400s for no match, bad host (no request, also `http`, lookalike host, `localhost`), HTML bytes as `image/jpeg`, over 10 MB, PNG under a JPEG header; 502 on a 503) |
| Manual: Audible match cover download | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |
| Manual: Open Library cover via archive.org redirect | Not run | Same |

Data checks for the developer (not run): `SELECT AudiobookId, Source, FileId, MimeType, length(Image) FROM AudiobookCover WHERE Source = 'provider';`

## Outcome (2026-10-19, commit 932d010, fix 8345381)

**Status:** Implemented
**Summary:** `PUT /api/groups/{id}/cover` with `{ source: "provider" }` downloads the stored match's cover through the new `CoverDownloadClient` and stores it in `AudiobookCover`. Only `https` URLs on `m.media-amazon.com` or `covers.openlibrary.org` are fetched, the type comes from the bytes' signature, and the body is cut off past 10 MB. `GET /cover` lists the match's cover as the `provider` option, and a failed download returns 502 without storing anything.
**Changed:** 7 files: new `Services/CoverDownloadClient.cs`, `Services/CoverService.cs` (`ChooseCoverAsync`, `ProviderCover`, `SignatureType`, `CoverHosts`), `Models/Api/MetadataDtos.cs` (`ProviderCoverDto`), `Controllers/GroupsController.cs`, `Program.cs`, new `Tests/Services/CoverDownloadClientTests.cs`, `Tests/Controllers/GroupCoverControllerTests.cs`. Plus 2 client files in the fix commit.
**Deviations:**
- `CoverService` is registered as transient because it holds the typed client, as `MetadataSearchService` does: *fine*.
- The body read has its own linked timeout, because `HttpClient.Timeout` stops at the headers with `ResponseHeadersRead`: *improvement*.
- `GroupsController` logs a warning with the cause before returning the generic 502 detail: *fine*.
- The host check uses `IdnHost`, case-insensitive. Extra tests cover `http` on an allowed host, a lookalike host, `localhost`, an over-10 MB body, a PNG under a JPEG header, and a request `fileId` being ignored: *improvement*.
- `UnresolvedFiles.tsx` and its test changed outside this slice's scope to fix the flaky test (a real bug: a quick tick right after a list loads was dropped): *bug*, fixed in `8345381` with the developer's OK.
**Follow-ups:**
- Slice 36: upload an image as a third `PUT /cover` source. Then the book page's cover picker.
- Manual checks for 33–35 (real Audible and Open Library downloads, the archive.org redirect) are still not run.
