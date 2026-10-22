# Slice 66: Rescan after processing

**Phase:** 9, Hardening & release
**Goal:** end-to-end tests prove that scanning, approving and processing fixture books, then scanning again, never creates a second group or a second set of copies for a processed book, whether its sources were deleted or kept.

## Done when
- [x] a test runs scan → approve → process → rescan with keep-source off: the rescan completes, `GET /api/groups` lists the same group ids and no unresolved or new groups, approving the processed book again is refused (400), and the library holds exactly one copy per file
- [x] the same with keep-source on: the sources are still in incoming, the rescan reports them unchanged, the groups are the same, approving again is refused, and the library is byte-for-byte unchanged by the rescan and the refused approval
- [x] an untouched second book scanned alongside still processes normally after the rescan (the rescan didn't disturb unprocessed books)
- [x] any behavior these tests show to be wrong is fixed with its failing test first, or stopped at gate 3 if the fix needs a product decision
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs`: `ScanTwoBooks` (~34), `Scan`, `GroupIds`, `ApproveJob`, `Approve`, `WaitForJob`, `Snapshot`, `Destinations`, `Sources`, and `Approval_marks_the_group_corrected_so_a_rescan_keeps_it_and_its_files` (~304), the closest existing test
- `src/AudiobookOrganizer.Api/Services/ScanService.cs` (~66: missing files are counted and their rows kept) and `Services/GroupingService.cs` (corrected groups and their files are skipped on regroup)
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs`: `Approve` and `Refusal` (the refusal order: errors, missing values, duplicates, existing destinations)
- `docs/CURRENT-STATE.md`, Decisions for slices 07, 13, 54 and 57

## Tasks
- [x] Add a helper in `JobsControllerTests.cs` that sets keep-source on or off through `PUT /api/settings` (reuse whatever the existing keep-source tests use; don't write a second one if there is one).
- [x] Add the keep-source-off test: `ScanTwoBooks`, approve and process book 1 to `Succeeded`, record the group ids, the unresolved list and the library files, then `Scan` again. Assert the latest scan is `Completed`, the group ids are unchanged, no unresolved or excluded files appeared, a second `Approve` of book 1 returns 400, `GET /api/jobs` still has one job for book 1, and the library files are the same list. Then approve book 2 and assert it runs to `Succeeded` with its own copies.
- [x] Add the keep-source-on test: as above with keep-source on, plus: book 1's sources still exist, the rescan's `filesUnchanged` counts them, and `Snapshot(_folders.Library)` is identical before the rescan and after the refused approval.
- [x] Run both. If either fails, find the root cause before changing anything: a fix in scanning, grouping or approval that fits this slice gets the failing test as its red step; a failure that needs a product choice (for example whether a processed book should leave the Incoming list) is gate 3: stop and report it. Run all four checks.
- Data: none
- Auth: none
- Accessibility: none

## Tests to add
- `JobsControllerTests`: after processing with keep-source off, a rescan leaves the groups unchanged, adds no unresolved files, and the processed book can't be approved again; an untouched book still processes.
- `JobsControllerTests`: after processing with keep-source on, a rescan reports the sources unchanged, leaves the groups and the library unchanged, and the processed book can't be approved again.

## Manual checks
- In the dev app, process one book from a temp incoming copy, then click "Scan incoming" on the dashboard: the Incoming page shows no new or unresolved entries for it, and its Approve on the Preview page is refused with the existing-file reason.

## Out of scope
- Hiding, archiving or marking processed books on the Incoming and Preview pages; they stay listed (with their existing destinations flagged) until the developer decides. Open question below.
- Removing `AudioFile` rows of deleted sources and the empty incoming folders left after deletion (same open question).
- Scanning the library folder or detecting books already in it.

## Notes
- Conflict check: matches PLAN.md phase 9's done-when "repeat scans don't duplicate processed books" and "end-to-end tests run against fixture audiobooks through scan → process"; existing tests cover approval-then-rescan (slice 54) but not processing-then-rescan. Expected to pass with today's code: approval marks the group `UserCorrected`, so rescans keep it and its files (missing ones too), and approval refuses existing destinations. This slice pins that down.
- Open question for the developer (added to CURRENT-STATE.md at review, the PLAN.md carried-in item from slice 57): after a book is processed, should it leave the Incoming and Preview lists, and should its `AudioFile` rows and empty incoming folders be removed once its sources are deleted?
- Files touched: `JobsControllerTests.cs`, this slice file, plus any fix the tests show is needed (expected none).
- Remaining phase 9 work: the processed-books question above, Docker docs for the home server and running there, and the carried-in items in PLAN.md.
