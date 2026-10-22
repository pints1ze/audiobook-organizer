# Slice 67: Hide processed books

**Phase:** 9, Hardening & release
**Goal:** a book whose processing job succeeded leaves the Incoming list, the Preview page and the dashboard book counts, and approving it again is refused with a clear reason.

## Done when
- [x] `GET /api/groups` doesn't list a group that has a `Succeeded` job; groups with only failed, queued or running jobs (or none) are still listed
- [x] `GET /api/preview` leaves the processed book out of its books and counts, and `GET /api/groups/{id}/preview` for it is 404
- [x] `GET /api/dashboard` book counts (total, by confidence, corrected) leave processed books out
- [x] `POST /api/jobs` for a processed book is 400 with "This book was already processed."
- [x] the book page (`GET /api/groups/{id}`, metadata, cover) still works for a processed book, and the Jobs page still lists its job
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs`: `GetGroups` (~71) and `CountGroups` (~293), the only list and count queries
- `src/AudiobookOrganizer.Api/Data/ProcessingRepository.cs`: `HasActiveJob` (~12)
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`: `Approve` (~45)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs`: `ScanTwoBooks`, `ApproveJob`, `WaitForJob`, `GroupList`, and the two slice 66 rescan tests (~655–750)
- `docs/CURRENT-STATE.md`, Decisions dated 2026-10-22 (processed books leave the lists)

## Tasks
- [x] Write the tests below in `JobsControllerTests.cs` first and watch them fail.
- [x] In `GroupRepository`, add one shared SQL condition (a `const string`, e.g. `NotProcessed`) — `NOT EXISTS (SELECT 1 FROM ProcessingJob j WHERE j.GroupId = FileGroup.Id AND j.Status = 'Succeeded')` (pass the status as a parameter from `ProcessingJob.Succeeded`) — and apply it in `GetGroups` and `CountGroups`. `GroupingService.List`, `NamingPreviewService.PreviewAll`/`Preview` and `DashboardService` then follow without changes. Leave `GetGroup` alone so the book page keeps working.
- [x] In `ProcessingRepository`, add `HasSucceededJob(long groupId)` beside `HasActiveJob`. In `ProcessingService.Approve`, before the preview lookup, return `Invalid` with `"This book was already processed."` (a `public const` on the service) when the group has a succeeded job. Without this the hidden preview would make it a 404.
- [x] Update the two slice 66 rescan tests: their second-approval assertion now expects the new message instead of "A file is already at …", and their group-list comparisons still hold (both lists are taken after processing). This follows the 2026-10-22 decision; it isn't a weakened test.
- [x] Run all four checks.
- Data: none (a query filter; no schema change)
- Auth: none
- Accessibility: none (no UI change; the pages just receive fewer books)

## Tests to add
- `JobsControllerTests`: after book 1 of `ScanTwoBooks` is processed, `GET /api/groups` lists only book 2, `GET /api/preview` has only book 2 and counts only its files, `GET /api/groups/{book1}/preview` is 404, and `GET /api/groups/{book1}` is still 200.
- `JobsControllerTests`: a book whose only job failed is still listed in `GET /api/groups` and `GET /api/preview` (reuse the existing failed-job setup in this file).
- `JobsControllerTests`: approving a processed book again returns 400 with "This book was already processed."
- `JobsControllerTests` (or `DashboardControllerTests` if it already scans and processes): the dashboard's book total drops by one after a book is processed.

## Manual checks
- In the dev app, process one book from a temp incoming copy: it disappears from Incoming and Preview and the dashboard count drops; its book page still opens from the Jobs page link (if there is one) or `/books/:id`.

## Out of scope
- Removing the processed book's `AudioFile` rows and empty incoming folders after its sources are deleted (next slice, same decision).
- Finishing a cut-off job's source deletions at startup (decided 2026-10-22; its own slice).
- A "processed" view or un-hiding processed books.
- Any client change; the pages show what the API returns.

## Notes
- Conflict check: matches the 2026-10-22 decision and PLAN.md phase 9's carried-in item. Retry stays unchanged: it only appears on a book's newest failed job, and a book with a succeeded job is hidden and refused anyway.
- Hidden books drop out of `PreviewAll`'s duplicate check. That's safe: their copies are on disk, so another book aiming at the same path is still flagged by `Exists`.
- Assumption: a book with a succeeded job and a later failed job (only possible through earlier retries) stays hidden; any succeeded job counts.
- Files touched: `GroupRepository.cs`, `ProcessingRepository.cs`, `ProcessingService.cs`, `JobsControllerTests.cs`, possibly `DashboardControllerTests.cs`, this slice file, PLAN.md (6–7).
- Remaining phase 9 work: `AudioFile` row and empty-folder cleanup after source deletion, finishing cut-off source deletions at startup, Docker docs and the home-server run, and the other carried-in items in PLAN.md.
