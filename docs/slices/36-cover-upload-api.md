# Slice 36: Cover upload API

**Phase:** 5, Metadata
**Goal:** the user can upload a JPEG or PNG as the book's cover, and the client has typed calls for every cover endpoint, ready for the picker.

## Done when
- [x] `PUT /api/groups/{id}/cover/upload` with `multipart/form-data` and one file field named `file` stores the image as the book's chosen cover (`source: "upload"`, `fileId: null`), creates the `Audiobook` if missing, marks the group `UserCorrected`, and returns the same body as `GET /api/groups/{id}/cover`.
- [x] The type comes from the bytes' signature (JPEG or PNG, as in slice 35), never from the file name or the part's content type. 400 (`file`) "Choose an image to upload." when the field is missing or empty; 400 (`file`) "The cover must be a JPEG or PNG."; 400 (`file`) "The cover can be at most 10 MB."; 404 for an unknown group. Nothing is stored on any error. The action has a request size limit a little over 10 MB, so a much larger body is refused before it's read.
- [x] `services/api.ts` has `getCovers(groupId)` (throws on non-200), `chooseCover(groupId, choice)` for the embedded and provider sources, and `uploadCover(groupId, file)` (a `FormData` PUT). Both return `{ ok: true, cover }`, `{ ok: false, errors }` (400), `{ ok: false, notFound: true }` (404), and `chooseCover` also returns `{ ok: false, downloadFailed: message }` (502). They throw for any other status. The URLs for the images are built by `fileCoverUrl(groupId, fileId)` and `chosenCoverUrl(groupId)`.
- [x] All four checks pass.

## Read first
- `docs/slices/35-provider-cover-download.md`, `## Outcome`
- `src/AudiobookOrganizer.Api/Services/CoverService.cs` (`ChooseCoverAsync`, `SignatureType`, `MaxCoverBytes`) and `Controllers/GroupsController.cs` (`PutCover`, `Invalid`)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs` (the cover DTOs)
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs` (`ScanBooks`, `CoverPng`, `CoverJpeg`, `CoverCount`)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`saveMetadata`, `pickMatch`), `services/api.test.ts`, `types/metadata.ts`

## Tasks
- [x] **Upload endpoint.** In `CoverService.cs` add `SaveUpload(groupId, byte[] image)` returning a `ChooseCoverResult`: check the signature and size with the existing helpers, save with `Source = "upload"`. In `GroupsController.cs` add `PUT {id:long}/cover/upload` taking `IFormFile? file`, with `[RequestSizeLimit]` and `[RequestFormLimits(MultipartBodyLengthLimit = ...)]` set to `MaxCoverBytes` plus 64 KB. It reads at most `MaxCoverBytes + 1` bytes from the file's stream and passes them on. Verify: `GroupCoverControllerTests`.
- [x] **Client types and calls.** In `types/metadata.ts` add `ChosenCover`, `EmbeddedCover`, `ProviderCover`, `BookCovers`, `CoverChoice` (`{ source: 'embedded'; fileId: number } | { source: 'provider' }`) and `SaveCoverResult`. In `services/api.ts` add the five functions from Done when. Verify: `api.test.ts`.
- Data: none (`AudiobookCover` already takes any source with a null `FileId`).
- Auth: none (single user, no login). The upload is size-limited before it's read and type-checked by content, and it's only stored in the database, never written to disk or served under its own name.
- Accessibility: none (no UI).

## Tests to add
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs`: uploading the PNG (sent as `cover.jpg` with content type `image/jpeg`) stores it as `source: "upload"`, `image/png`; `/cover/image` returns the same bytes and the group reads `userCorrected: true`. Uploading again replaces it.
- Same file: 400 (`file`) with no file field, with an empty file, with HTML bytes named `cover.png`, and with a file of `MaxCoverBytes + 1` bytes that starts with the PNG signature; 404 for an unknown group. `AudiobookCover` stays empty each time.
- `src/AudiobookOrganizer.Client/src/services/api.test.ts`: `uploadCover` sends a PUT to `/api/groups/4/cover/upload` whose `FormData` holds the file under `file`, and maps 200, 400 and 404; `chooseCover` sends `{ source: 'provider' }` as JSON and maps 502 to `downloadFailed` with the problem's detail; `getCovers` returns the parsed body.

## Manual checks
- Run `scripts/start-api.ps1`, then upload an image: `curl.exe -X PUT -F "file=@C:\path\to\cover.jpg" http://localhost:5070/api/groups/{id}/cover/upload`. Open `/api/groups/{id}/cover/image`: the uploaded image shows.
- Upload a 20 MB file and confirm it's refused quickly and nothing changes.

## Out of scope
- The cover picker on the book page (next slice, 37)
- Cropping, resizing or converting an upload; formats other than JPEG and PNG
- Removing the chosen cover (no "no cover" choice yet)

## Notes
- File count: 6 (`CoverService.cs`, `GroupsController.cs`, `GroupCoverControllerTests.cs`, `types/metadata.ts`, `services/api.ts`, `services/api.test.ts`).
- A separate route rather than a third source on `PUT /cover`, because the body is `multipart/form-data`, not JSON.
- The client calls come in this slice so the picker (37) touches only components and the book page.
- MVC controllers don't apply antiforgery to `IFormFile` (only minimal APIs do), and the app has no cookies or auth, so there's nothing to forge.
- Conflict check: no new package. Uploads go only into SQLite, so "only the configured directories may be read or written" still holds.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 184 passed, 0 failed | Was 182 |
| Frontend tests | 183 passed, 0 failed | Was 176 |
| New tests | 3 of 3 present, passing | `GroupCoverControllerTests` (+2: PNG sent as `cover.jpg`/`image/jpeg` stored as `upload`/`image/png`, replaced by a second upload; 400s for no field, empty file, HTML named `cover.png`, `MaxCoverBytes + 1` bytes, and 404, storing nothing); `api.test.ts` (+7: `getCovers`, `chooseCover` JSON + 502 `downloadFailed`, `uploadCover` FormData + 200/400/404, image URLs) |
| Manual: curl upload, image shows | Not run | API (5070) not running. Start with `scripts/start-api.ps1` |
| Manual: 20 MB upload refused quickly | Not run | Same. The test server may not enforce `[RequestSizeLimit]`, so this is only checkable against Kestrel |

Data checks for the developer (not run): `SELECT AudiobookId, Source, MimeType, length(Image) FROM AudiobookCover WHERE Source = 'upload';`

## Outcome (2026-10-19, commit 605c4d2)

**Status:** Implemented
**Summary:** `PUT /api/groups/{id}/cover/upload` takes a multipart `file`, reads at most 10 MB + 1 byte under a request size limit, types it by its signature, and stores it as the `upload` cover. The client has typed calls for every cover endpoint (`getCovers`, `chooseCover`, `uploadCover`, `fileCoverUrl`, `chosenCoverUrl`) and the matching types.
**Changed:** 6 files: `Services/CoverService.cs` (`SaveUpload`), `Controllers/GroupsController.cs` (`PutCoverUpload`, `ReadAtMost`), `Tests/Controllers/GroupCoverControllerTests.cs`, `Client/src/types/metadata.ts`, `Client/src/services/api.ts`, `Client/src/services/api.test.ts`.
**Deviations:**
- `uploadCover` throws on a 502 rather than returning `downloadFailed`; only `chooseCover` downloads: *fine*.
- `uploadCover` sends no Content-Type, so the browser sets the multipart boundary (tested): *fine*.
- A request that isn't multipart at all gets ASP.NET's 415 from `[ApiController]`'s consumes rule, untested: *fine*.
- The 20 MB refusal can't be tested in the test server: *debt*, covered by the manual check.
**Follow-ups:**
- Slice 37: the cover picker on the book page (embedded, the match's cover, upload; the chosen cover shown). It should finish phase 5.
- Manual checks for 33–36 are still not run.
