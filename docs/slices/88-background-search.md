# Slice 88: Background search

**Phase:** 11, Parallel identification
**Goal:** after each completed scan, and at startup, the server searches every unprocessed, unmatched book that has no stored search, one at a time with a 2-second gap, and stores its candidates.

## Done when
- [x] a hosted `CandidateSearchWorker` searches, one at a time, every group with no `CandidateSearch` row, no chosen match and no processing job, storing each result through `CandidateService.RefreshAsync`
- [x] it waits `CandidateSearch:DelayMs` (default 2000) between two searches, and runs at startup and again whenever a scan completes
- [x] a book that already has a stored search (including a stored error) or a chosen match isn't searched again by the worker
- [x] a failure while searching one book is logged and the worker goes on to the next; shutdown stops it between searches
- [x] integration tests don't start the worker unless they ask for it, so no test calls a real provider
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docs/CURRENT-STATE.md` Decisions, 2026-10-25 (developer, after slice 87): throttle and which books
- `src/AudiobookOrganizer.Api/Services/ProcessingWorker.cs` (the wake/drain pattern to copy)
- `src/AudiobookOrganizer.Api/Services/CandidateService.cs` and `Data/CandidateRepository.cs` (slice 86)
- `src/AudiobookOrganizer.Api/Services/ScanRunner.cs` (where a scan finishes)
- `src/AudiobookOrganizer.Api/Data/GroupRepository.cs` (`NotProcessed`), `Configuration/ToolSettings.cs` and `Program.cs` (settings and registrations)
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizerWebApplicationFactory.cs` (`RunProcessingWorker`) and `Controllers/GroupCandidatesControllerTests.cs` (scan seeding, fake providers)

## Tasks
- [x] **Which books.** In `Data/CandidateRepository.cs` add `GetUnsearchedGroupIds()`: ids of `FileGroup` rows with no `CandidateSearch` row, no `MetadataMatch` (through `Audiobook`) and no `ProcessingJob` of any status, ordered by `Id`. Verify with the worker tests.
- [x] **Settings.** New `Configuration/CandidateSearchSettings.cs` (`SectionName = "CandidateSearch"`, `int DelayMs = 2000`), bound in `Program.cs` like `ToolSettings`.
- [x] **Worker.** New `Services/CandidateSearchWorker.cs`, a `BackgroundService` with the same `Wake()` and drain loop as `ProcessingWorker` (starts signalled, merges wakes). Each pass resolves a `CandidateService` from `IServiceProvider` (it's transient and holds typed `HttpClient`s), takes `GetUnsearchedGroupIds()` and for each id calls `RefreshAsync`, then `Task.Delay(DelayMs, stoppingToken)` before the next (no delay after the last). A group gone by then (`RefreshAsync` returns null) is skipped. Any exception other than cancellation is logged with the group id and the pass goes on; cancellation ends it. Register it as a singleton plus hosted service in `Program.cs`, as `ProcessingWorker` is.
- [x] **Wake after a scan.** In `ScanRunner.Execute`, when `scans.Execute` returns a run with status `Completed`, call `CandidateSearchWorker.Wake()`. In the test factory add `RunCandidateSearchWorker` (default false) that removes the worker's hosted registration the same way `RemoveProcessingWorker` does; tests that turn it on set `CandidateSearch:DelayMs` to 0 or a small value.
- [x] **Tests** (below), then all four checks.
- Data: none (reads the slice 86 tables).
- Auth: none (no auth in the app).
- Accessibility: none (no UI in this slice).

## Tests to add
- `Api.Tests/Services/CandidateSearchWorkerTests.cs` (integration, worker on, fake providers): after a scan completes, every new book gets a stored search without any `POST`, with the Audible fixture's candidates.
- Same file: a book with a chosen match and a book with a stored search before the worker's pass are not searched again (the fake handler's request count stays at the expected number).
- Same file: with both providers unavailable, each book stores the error and the worker keeps going through every book.
- Same file: with `DelayMs` set to e.g. 300, two books' `SearchedAtUtc` values are at least that far apart.

## Manual checks
- Restart the dev API with a few new books in incoming and run a scan: within about 2 s per book the Review pane shows suggested matches for each without pressing "Find suggestions"; the API log shows one search every ~2 s.

## Out of scope
- Showing search progress in the UI or a list badge for "has suggestions".
- Carrying candidates across a rescan that regroups an uncorrected book (decided: searched again).
- Batch accept and batch approve (next slices).

## Notes
- Conflict check: matches are never applied (only `RefreshAsync`, which stores candidates). The throttle and the "which books" rule are the developer's 2026-10-25 answers; the PRD's throttle `[NEEDS CLARIFICATION]` is resolved. "No processing job of any status" also skips Queued and Failed books, which already passed approval.
- The settings class exists so tests can shorten the delay; it's not a user-facing option.
- Existing integration tests run scans with real provider registrations; removing the worker by default is what keeps them off the network.
- Files touched: `CandidateRepository.cs`, `CandidateSearchSettings.cs` (new), `CandidateSearchWorker.cs` (new), `ScanRunner.cs`, `Program.cs`, `AudiobookOrganizerWebApplicationFactory.cs`, `CandidateSearchWorkerTests.cs` (new): 7.
- Later slices for this phase: select books and "Accept suggested match" in one action; select Ready books and approve in one action.
