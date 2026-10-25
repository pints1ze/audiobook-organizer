# Slice 77: Review list API

**Phase:** 10, Review workspace
**Goal:** `GET /api/review` returns every unprocessed book with what the workspace list needs (title, author, narrator, runtime, cover source) and its stage, plus counts per stage.

## Done when
- [x] `GET /api/review` returns `{ books: [...], counts: { needsMatch, needsReview, ready, queued, failed } }`, books in `GET /api/groups` order. Each book: `groupId`, `title`, `author`, `narrator` (first narrator or null), `durationMs`, `fileCount`, `stage`, `coverSource` (`"chosen"`, `"embedded"` or null) and `coverFileId` (the first file with an embedded cover when `coverSource` is `"embedded"`, else null).
- [x] Title, author and narrator are the book's metadata values (`GET /api/groups/{id}/metadata`, including its file fallback), not just the group's.
- [x] `stage` follows these rules, first match wins:
  1. the group's newest job is `Queued` or `Running` → `Queued`
  2. the newest job is `Failed` → `Failed`
  3. no chosen match and no user-entered metadata value → `NeedsMatch`
  4. its preview has no file with missing tokens, an error, a duplicate or an existing destination → `Ready`
  5. otherwise → `NeedsReview`
- [x] Processed books (a `Succeeded` job) are not listed, as in `GET /api/groups` (slice 67). Unresolved and excluded files are not books and aren't listed.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/CURRENT-STATE.md`, `docs/UIUX.md` (Books: stage chips)
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingService.cs` (`List`), `Services/AudiobookService.cs` (`GetMetadata`, `GetMatch`), `Services/NamingPreviewService.cs` (`PreviewAll`), `Services/ProcessingService.cs` (`GetJobs`), `Services/CoverService.cs` (`GetCovers`)
- `src/AudiobookOrganizer.Api/Controllers/DashboardController.cs` and `Services/DashboardService.cs` as the model for a read-only summary endpoint
- `src/AudiobookOrganizer.Api.Tests/Controllers/GroupCoverControllerTests.cs`, `JobsControllerTests.cs` and `PreviewControllerTests.cs` for how tests set up groups, matches, covers and jobs

## Tasks
- [x] DTOs: `ReviewBookDto`, `ReviewCountsDto`, `ReviewListDto` in `Models/Api/ReviewDtos.cs`; `stage` as a string (`NeedsMatch`, `NeedsReview`, `Ready`, `Queued`, `Failed`), like `Confidence`.
- [x] `Services/ReviewService.cs`: builds the list from the existing services (one `PreviewAll`, one `GetJobs`, per-book metadata and cover lookups). Keep the stage rule in one small static method so it is unit-testable. Mark the per-book lookups `// shortcut: one metadata and cover read per book — revisit when the list is slow (hundreds of books)`.
- [x] `Controllers/ReviewController.cs` (`GET /api/review`, thin) and the DI registration in `Program.cs`.
- [x] Tests (below).
- Data: none (read-only; no schema change)
- Auth: none
- Accessibility: none (no UI)

## Tests to add
- `Api.Tests/Services/ReviewStageTests.cs`: the stage rule for each case: queued job, running job, failed job (newest wins over an older succeeded/failed one), no match and no user values, a match with a clean preview, a match with a missing token, a user-edited book with no match and a clean preview (`Ready`).
- `Api.Tests/Controllers/ReviewControllerTests.cs`: after a scan of fixture books, `GET /api/review` lists them as `NeedsMatch` with title, author and duration, and the counts match; after picking a match and choosing a cover, that book is `Ready` (or `NeedsReview` when its preview has a problem) with `coverSource: "chosen"`; a book whose job succeeded isn't listed.
- `ReviewControllerTests.cs`: a book with an embedded cover and no chosen one has `coverSource: "embedded"` and its file's id.

## Manual checks
- With the API running against the dev data: `curl http://localhost:5070/api/review` returns the 26 books, all `NeedsMatch` except any with a match, and responds in well under a second.

## Out of scope
- The client (types, `services/api.ts`, the list UI): slice 78.
- A `Done` list of processed books, stored candidates and scores (phase 11).

## Notes
- Conflict check: "Ready" doesn't change approval; `POST /api/jobs` still refuses a book whose preview isn't clean (slice 54), so the rule mirrors it. A user-completed book with no match counts as identified (developer, 2026-10-19: manual completion needs no separate state).
- "User-entered metadata value" means any `AudiobookField` with origin `user` (slice 32); the file fallback doesn't count.
- About 5 new files plus `Program.cs`, under the cap.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 414 passed, 0 failed | |
| Frontend tests | 277 passed, 0 failed | 17 files |
| New tests | 3 of 3 present, passing | `ReviewStageTests` (7 cases); `ReviewControllerTests` (5 cases: scan → NeedsMatch with counts, match + cover → Ready/NeedsReview with `chosen`, succeeded job not listed, embedded cover) |
| Manual: `curl /api/review` on the dev data | Passed | 200 in 0.12 s; 26 books: 25 NeedsMatch, 1 NeedsReview ("Don't Let Him In": metadata title, narrator, chosen cover) |

## Outcome (2026-10-25, commit e197d23)

**Status:** Implemented
**Summary:** `GET /api/review` lists every unprocessed book with metadata title, first author and narrator, runtime, file count, cover source and a stage (Queued, Failed, NeedsMatch, Ready, NeedsReview, first rule wins), plus counts per stage. The rule is `ReviewService.Stage`, a static method.
**Changed:** 8 files: `Models/Api/ReviewDtos.cs`, `Services/ReviewService.cs`, `Controllers/ReviewController.cs`, `Program.cs`, `Services/ReviewStageTests.cs`, `Controllers/ReviewControllerTests.cs`, this slice file, `docs/PLAN.md`.
**Deviations:**
- `author` is the first author, like `narrator`: *fine*; the list shows one name.
- Controller tests were written after the service, so they weren't seen failing first (the stage tests were): *fine*.
- `ReviewService` is transient because `CoverService` is: *fine*.
**Follow-ups:**
- Next: slice 78, the workspace list at `/` with covers and the stage filter (client types and `getReview` in `services/api.ts`).
