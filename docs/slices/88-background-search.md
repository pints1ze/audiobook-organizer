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

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings; `-p:OutDir=<scratchpad>` because the running dev API locks `bin/Debug` |
| Typecheck | Passed | |
| Backend tests | 431 passed, 0 failed | |
| Frontend tests | 359 passed, 0 failed | 21 files. During implementation one run failed `JobsPage.test.tsx > offers Retry only on a book's newest failed job and queues it again`: Failed (accepted), the slice 62 flake; no client code changed |
| New tests | 4 of 4 present, passing | `CandidateSearchWorkerTests`: search after a scan without a POST; matched or searched books skipped; providers down stores errors and goes on; configured delay between searches |
| Manual: scan new books with the dev API, suggestions appear ~2 s apart | Not run | needs the dev API restarted (`scripts/start.ps1`) and real books in incoming |

## Outcome (2026-10-25, commit 17422a3)

**Status:** Implemented
**Summary:** A hosted `CandidateSearchWorker` searches every book with no stored search, no chosen match and no processing job, one at a time with `CandidateSearch:DelayMs` (2000) between searches, at startup and after each completed scan (`ScanRunner` wakes it). Failures are logged per book and the pass goes on; integration tests leave the worker off unless they turn it on.
**Changed:** 7 code files (`Services/CandidateSearchWorker.cs` and `Configuration/CandidateSearchSettings.cs` new, `CandidateRepository.cs`, `ScanRunner.cs`, `Program.cs`, the test factory, `CandidateSearchWorkerTests.cs` new)
**Done when:** all 6 met; manual check not run.
**Deviations:**
- Tests add one or two extra books (copies of the M4B fixture in subfolders): fine.
- The "not searched again" test seeds on a host without the worker, then starts a second host on the same database with it: fine, tests the startup pass.
- `RemoveCandidateSearchWorker` helper in the test factory, matching `RemoveProcessingWorker`: fine.
**Follow-ups:**
- A pass works from the id list it read at its start; groups replaced by a rescan mid-pass are skipped (still delayed for) and the scan's wake runs another pass. Fine for now.
