# Slice 07: Scan inventory (read-only)

**Phase:** 2, Settings & scan
**Goal:** a `ScanService` recursively inventories every MP3 and M4B under the configured incoming directory into SQLite, with tags, duration, cover presence, chapter count, size and modified time. It skips unchanged files on rescan, records unreadable files instead of failing, and never modifies anything in incoming.

## Done when
- [x] `ScanService.Run()` creates a `ScanRun` row and, for every `.mp3` and `.m4b` (case-insensitive) under incoming including nested folders, an `AudioFile` row with the fields listed under Data. Other files are ignored
- [x] A second scan with no file changes re-reads nothing, re-checks only size and mtime, and reports every file as unchanged. A file whose size or mtime changed is re-read and its row updated. A new file is added. A file that's gone is counted as missing (its row is kept)
- [x] A corrupt `.mp3` gets an `AudioFile` row with `ScanError` set and is counted as failed. The rest of the scan completes with status `Completed`
- [x] After a scan, every file in incoming has the same bytes and the same last-write time as before (proven by a test)
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (`Models/Local/` for SQLite entities, Dapper repositories in `Data/`, logic in `Services/`), Environment (file safety: scans are read-only), the External tools rule (ffprobe only through `FfmpegRunner`)
- `docs/PRD.md`: workflow 2 (Scan), Data (AudioFile, ScanRun), Rules (scanning never modifies files)
- `overview.md` FR-02 (the properties to collect) and section 12 (an unreadable file must not end the scan)
- `src/AudiobookOrganizer.Api/Data/DatabaseInitializer.cs`, `Data/SettingsRepository.cs` (repository style), `Services/ChapterReader.cs`, `Services/SettingsService.cs`
- `docs/fixtures/audio/README.md` (fixture tags: title `Fixture Book`, artist `Fixture Author`, 3 chapters, 30 s)

## Tasks
- [x] **Schema and entities.** In `DatabaseInitializer`, add `CREATE TABLE IF NOT EXISTS` for:
  - `ScanRun`: `Id` INTEGER PK, `StartedAt`, `FinishedAt` (null while running), `Status` (`Running`, `Completed` or `Failed`), `FilesFound`, `FilesAdded`, `FilesUpdated`, `FilesUnchanged`, `FilesMissing`, `FilesFailed`, `Error`.
  - `AudioFile`: see Data.

  Add `Models/Local/ScanRun.cs` and `Models/Local/AudioFile.cs` to match. Timestamps are stored as ISO-8601 UTC text. Verify: the app starts and the tables exist (covered by the tests below).
- [x] **File metadata reader.** Add `Services/AudioFileReader.cs` with `AudioFileMetadata Read(string path)`. It uses TagLibSharp **read-only**: `TagLib.File.Create` and never `Save`. It fills title, album, artist (performers joined with "; "), album artist, composer, genre, year, track, disc, comment, duration in milliseconds and `HasCover` (any embedded picture). It adds `ChapterCount` from `ChapterReader`. Exceptions propagate so the scanner can record them. Verify: the reader test below against both fixtures.
- [x] **Repository.** Add `Data/ScanRepository.cs` (Dapper) with:
  - insert, update and finish for `ScanRun`
  - `GetFilesByPathPrefix` or similar to load existing `AudioFile` rows for incoming
  - upsert of an `AudioFile` by `SourcePath`
  - `MarkSeen(ids, scanId)` for unchanged files

  Each scan's writes for one file are atomic, and the whole scan doesn't need one transaction. Verify: through the service tests.
- [x] **Scan service.** Add `Services/ScanService.cs` with `ScanRun Run(CancellationToken)`.
  1. Read incoming from `SettingsService`. If it isn't configured or no longer exists, finish the run as `Failed` with a plain `Error` message.
  2. Enumerate files recursively with `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = ReparsePoint }`, which skips symlinks.
  3. Filter by extension.
  4. For each file: if a known row has the same size and last-write UTC, it's unchanged, so only update `LastSeenScanId`. Otherwise read it through `AudioFileReader` and upsert. On an exception, upsert the row with `ScanError` set to the exception message (file system fields still filled), count it failed, and log a warning.
  5. Count missing as known rows under incoming not seen in this run.
  6. Finish as `Completed`. Check cancellation between files.

  Register the new services in `Program.cs`. Verify: the tests below.
- Data: two new tables, created at startup.
  - `AudioFile` columns: `Id` INTEGER PK, `SourcePath` TEXT UNIQUE (absolute), `FileName`, `Extension` (lowercase, no dot), `SizeBytes`, `ModifiedAtUtc`, `DurationMs`, `Title`, `Album`, `Artist`, `AlbumArtist`, `Composer`, `Genre`, `Year`, `Track`, `Disc`, `Comment`, `HasCover` (0/1), `ChapterCount`, `ScanError`, `FirstSeenScanId`, `LastSeenScanId`.
  - A `SourcePath` index comes from the UNIQUE constraint. Path comparison is the stored absolute path as enumerated (no case folding).
- Auth: none (no authentication by decision). Only the configured incoming directory is read.
- Accessibility: none (no UI in this slice).

## Tests to add
- `Services/AudioFileReaderTests.cs`: for `chapters.m4b` and `chapters.mp3`, the result has title `Fixture Book`, artist `Fixture Author`, duration of about 30,000 ms, `ChapterCount` 3, and `HasCover` false.
- `Services/ScanServiceTests.cs`: a temp incoming with a nested `disc 1/` folder holding both fixtures, a `notes.txt` and a `cover.jpg`. The first scan finds 2 files, adds 2 and ignores the rest, and the rows have the expected fields.
- `Services/ScanServiceTests.cs`: the second scan reports 2 unchanged and 0 updated. Touching one file's mtime then gives 1 updated. Adding a third gives 1 added. Deleting one gives 1 missing.
- `Services/ScanServiceTests.cs`: a corrupt `broken.mp3` (random bytes) gives one row with `ScanError` set, `FilesFailed` 1, and run status `Completed`.
- `Services/ScanServiceTests.cs`: every incoming file's SHA-256 and `LastWriteTimeUtc` are identical before and after a scan. An unconfigured incoming gives a `Failed` run with an error message.

## Manual checks
- Copy a few real audiobooks into a temp folder (one M4B, and one multi-file MP3 book in nested folders). Save that folder as incoming through the Settings page, then run a scan. There's no endpoint until slice 08, so use a throwaway test run or the test harness. Inspect with `SELECT FileName, Title, Artist, DurationMs, ChapterCount, HasCover, ScanError FROM AudioFile;` and confirm the values look right. Optional, since slice 08 makes this a one-click check.

## Out of scope
- An HTTP endpoint, background execution and one-scan-at-a-time locking: slice 08.
- Dashboard counts and the Scan button: slice 08.
- Grouping files into books (phase 3) and recording cover-art image files like `cover.jpg` as cover candidates: phase 3 or 5, whichever needs them first.
- Formats other than MP3 and M4B (e.g. `.m4a`): later, if the developer asks.
- Removing rows for missing files: they're counted only. Cleanup is decided when grouping exists.

## Notes
- **Conflict check:** consistent with AGENTS.md (layout, no new packages, ffprobe through `FfmpegRunner` as used by `ChapterReader`, never saving with TagLibSharp), the PRD (read-only scan, unchanged files recognised on rescan) and FR-02. The "preserve saved user corrections" part of FR-02 is about groups, so it lands in phase 3 and 4 with the group tables. `AudioFile` rows keep stable `Id`s across rescans so later group links survive.
- **Performance:** M4B chapter reads spawn one ffprobe process per changed file. That's fine for now; unchanged files never spawn it. Revisit if large first scans are slow.
- **Size:** 4 tasks, 9 files: `DatabaseInitializer.cs`, `ScanRun.cs`, `AudioFile.cs`, `ScanRepository.cs`, `AudioFileReader.cs`, `ScanService.cs`, `Program.cs` and 2 test files.
- **Commit:** `feat: slice 07 scan-inventory`.
- **Remaining in phase 2:** 08, scan API and dashboard.
  - `POST /api/scans` starts a background scan (409 if one is already running) and `GET /api/scans/latest` returns its status and counts.
  - `GET /api/dashboard` (or similar) returns the configured directories, last scan time and file counts.
  - The dashboard shows them with a "Scan incoming" button and the scan's progress.
