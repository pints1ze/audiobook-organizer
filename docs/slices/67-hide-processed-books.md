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

## Test results (2026-10-22)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 391 passed, 0 failed | |
| Frontend tests | 271 passed, 0 failed | the accepted `JobsPage` Retry flake didn't occur |
| New tests | 4 of 4 covered, passing | `A_processed_book_leaves_the_groups_list_the_preview_and_the_dashboard_counts_but_its_book_page_stays` (lists, preview 404, dashboard counts, book page), `A_book_whose_only_job_failed_is_still_listed_and_previewed`; the re-approval 400 is covered by the updated `Approving_a_book_whose_job_succeeded_is_refused_so_it_is_never_copied_twice` |
| Manual: process a book in the dev app; it leaves Incoming, Preview and the dashboard count, book page still opens | Not run | dev API not running; start with `scripts/start.ps1` |

## Outcome (2026-10-22, commit 4887d68)

**Status:** Implemented
**Summary:** `GroupRepository.GetGroups` and `CountGroups` leave out any group with a `Succeeded` job, so processed books drop out of Incoming, Preview and the dashboard book counts; `GetGroup` is unchanged, so the book page still works. `ProcessingService.Approve` refuses a processed book with 400 "This book was already processed." before it looks up the preview.
**Changed:** 5 files: `Data/GroupRepository.cs` (shared `NotProcessed` filter), `Data/ProcessingRepository.cs` (`HasSucceededJob`), `Services/ProcessingService.cs` (`AlreadyProcessed`), `Tests/Controllers/JobsControllerTests.cs` (2 new tests, 3 updated), the slice file.
**Deviations:**
- The slice 66 rescan tests' group-list comparisons didn't "still hold" as the slice said: they now expect only book 2's group, because book 1 is hidden. This follows the 2026-10-22 decision and isn't a weakened test: *fine*.
- A third existing test (`Approving_a_book_whose_job_succeeded_is_refused_so_it_is_never_copied_twice`) now expects the new message and covers the planned re-approval test, so no duplicate was added: *fine*.
- No `DashboardControllerTests` change: the dashboard counts are asserted in the new `JobsControllerTests` test: *fine*.
**Follow-ups:**
- Remove a processed book's `AudioFile` rows and empty incoming folders once its sources are deleted (next slice, 2026-10-22 decision).
- Manual (developer): process a book in the dev app; it leaves Incoming, Preview and the dashboard count, and its book page still opens.
