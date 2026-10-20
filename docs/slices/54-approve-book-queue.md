# Slice 54: Approve a book into the processing queue

**Phase:** 8, Processing
**Goal:** `POST /api/jobs` approves one book: if its preview is clean it records a `Queued` processing job with each file's source and destination snapshotted, and `GET /api/jobs` lists the jobs. Nothing is copied yet.

## Done when
- [ ] `POST /api/jobs` with `{ groupId }` returns 201 and the job (`id`, `groupId`, `title`, `status: "Queued"`, `createdAtUtc`, `files` in book order with `fileId`, `sourcePath`, `destinationPath`, `status: "Pending"`) when every file of the book's preview has a destination and no missing values, duplicate or existing flag
- [ ] it refuses with a 400 problem whose detail names the reason for: a book with no files, any file with an error, missing values, a duplicate target or an existing destination; 404 for an unknown group; 409 when the book already has a `Queued` or `Running` job
- [ ] approving marks the group `UserCorrected`, so a rescan keeps the book and its files
- [ ] `GET /api/jobs` lists jobs newest first with their files; `GET /api/jobs/{id}` returns one or 404
- [ ] the incoming and library folders are untouched by approval (no file or folder is created); all four checks pass

## Read first
- `docs/slices/52-library-preview-api.md` and its Outcome (preview flags)
- `src/AudiobookOrganizer.Api/Services/NamingPreviewService.cs` (`Preview(groupId)`) and `Models/Api/PreviewDtos.cs`
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs` (table style, cascade comments) and `Data/GroupRepository.cs` (`UpdateTitleAuthor` sets `UserCorrected`)
- `src/AudiobookOrganizer.Api/Models/Local/ScanRun.cs` (status constants) and `Controllers/ScansController.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/PreviewControllerTests.cs` (`ScanTwoBooks`, `SaveTemplates`, temp folders)

## Tasks
- [ ] **Schema and entity.** `DatabaseInitializer`: `ProcessingJob` (`Id INTEGER PRIMARY KEY AUTOINCREMENT`, `GroupId INTEGER REFERENCES FileGroup (Id) ON DELETE SET NULL` so history outlives the group, `Title TEXT`, `Status TEXT NOT NULL`, `CreatedAtUtc`, `StartedAtUtc`, `FinishedAtUtc`, `Error`) and `ProcessingJobFile` (`JobId ... ON DELETE CASCADE`, `AudioFileId INTEGER NOT NULL`, `Position`, `SourcePath`, `DestinationPath`, `Status TEXT NOT NULL`, `Error`, `PRIMARY KEY (JobId, Position)`), plus an index on `ProcessingJob (GroupId)`. New `Models/Local/ProcessingJob.cs` with `ProcessingJob`, `ProcessingJobFile` and status constants (`Queued`, `Running`, `Succeeded`, `Failed`; files `Pending`).
- [ ] **Repository.** New `Data/ProcessingRepository.cs`: `HasActiveJob(groupId)`, `CreateJob(job, files)` in one transaction that also sets the group's `UserCorrected = 1`, `GetJobs()` (newest first, files ordered by `Position`) and `GetJob(id)`.
- [ ] **Service.** New `Services/ProcessingService.cs`: `Approve(groupId)` returns a result (`NotFound`, `Conflict`, `Invalid` with a message, or the job). It takes `NamingPreviewService.Preview(groupId)` and refuses in this order: no files ("The book has no files."), an error (the first file's error), missing values ("Fill in the missing values: series, year." from the union), a duplicate ("Two files would go to the same place: <relative path>."), an existing file ("A file is already at <relative path>."). `Title` is the group title. Register in `Program.cs`.
- [ ] **Controller and DTOs.** New `Models/Api/ProcessingDtos.cs` (`ProcessingJobDto`, `ProcessingJobFileDto`, `ApproveRequest`) and `Controllers/JobsController.cs`: `POST /api/jobs` (201 with `Location`, 400/404/409 problems), `GET /api/jobs`, `GET /api/jobs/{id}`. A missing or non-positive `groupId` is a 400.
- [ ] **Tests.** New `Controllers/JobsControllerTests.cs` (below). Run all four checks with full backend output (TRX).
- Data: two new tables, created with `CREATE TABLE IF NOT EXISTS` at startup; no change to existing tables.
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- `JobsControllerTests`: approving a clean scanned book returns 201 with `Queued`, its files `Pending` in book order with the preview's destinations; `GET /api/jobs` and `GET /api/jobs/{id}` return it; the library folder is still empty.
- `JobsControllerTests`: a duplicate target (file template `%album%` on a two-file book) and an existing destination (a file placed at the preview's destination) each give 400 with the reason; no job is recorded.
- `JobsControllerTests`: a book with missing values (directory template using `%series%` outside `[ ]`) gives 400 naming the token; an unknown group gives 404; a missing body `groupId` gives 400.
- `JobsControllerTests`: a second approve of the same book gives 409.
- `JobsControllerTests`: after approval, a rescan keeps the group id and its files (`GET /api/groups` still lists it).

## Manual checks
- With the API running and a scanned book whose preview is clean, `POST /api/jobs` with its group id: 201 and the job; the library folder shows no new files or folders. A second POST gives 409.
- `POST` a book that `/preview` flags (e.g. "Duplicate target"): 400 with a readable detail.

## Out of scope
- Running jobs: copying, tagging, verifying, deleting sources (next slices)
- Retry, cancel and job history UI; an Approve button on the book or Preview page
- Blocking corrections to a book while it's queued (the job uses its snapshot)

## Notes
- File count: 8 (`DatabaseInitializer.cs`, `Models/Local/ProcessingJob.cs`, `Data/ProcessingRepository.cs`, `Services/ProcessingService.cs`, `Models/Api/ProcessingDtos.cs`, `Controllers/JobsController.cs`, `Program.cs`, `Tests/Controllers/JobsControllerTests.cs`). At the cap.
- Why next: phase 8's first piece. Every later step (copy, tag, verify, delete, retry) needs a job and its files to work on, and approval is where the PRD rule "nothing is moved or tagged without explicit per-book approval" lives.
- Assumption (reversible): missing values block approval as well as errors, duplicates and existing files, so no book is filed with an empty token. The user fixes them in the editor or the templates. Flag in review if the developer wants missing values allowed.
- Destinations are snapshotted at approval so the job does exactly what the user approved; the worker will still refuse to overwrite anything that appears there later (PRD rule).
- Conflict check: consistent with PRD rules (approval per book, conflicts block), PLAN phase 8, and the excluded-files decision: excluded files are never in a group, so they're never in a job. The tag-frame open question isn't touched here. No new packages.
- Split off for later slices: a background `ProcessingRunner` (one job at a time, copy to a temp name then move into place, never overwrite); tag and cover writing on the copy with the track as book position; read-back verify and source delete unless keep-source; failure details and retry without duplicates; the jobs UI and an Approve button.
