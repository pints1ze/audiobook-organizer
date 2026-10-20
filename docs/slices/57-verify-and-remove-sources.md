# Slice 57: Read each copy back, then remove the sources

**Phase:** 8, Processing
**Goal:** a job reads every tagged copy back and fails it if the tags, cover, chapters or duration are wrong; once every file of the job is verified, it deletes the sources unless "keep source files" is on.

## Done when
- [x] each copy is read back (`AudioFileReader`) after it moves into place and must have the book title, the first artist, track = its position, a cover when the book has a chosen cover or the source had one, the source's chapter count, and a duration within 1 second of the source's; a mismatch fails the file and job with a message naming the check (e.g. "The copy of X has 2 chapters; the source has 3."), deletes that copy and removes the job's other copies as in slice 55
- [x] with "keep source files" off (the default), a job whose files all verified deletes every source and its files end `SourceDeleted`; with it on, every source stays and the files end `Copied`
- [x] sources are deleted only after the whole job verified: a job failing on any file deletes no source
- [x] a source whose size or last-write time changed since its copy was made is kept, and a source that can't be deleted is kept; either way the file stays `Copied` with an `Error` saying so ("The source changed after it was copied, so it was kept." / "The copy is in place, but the source couldn't be deleted: …") and the job still ends `Succeeded`
- [x] all four checks pass

## Read first
- `docs/slices/56-tag-copies.md` and its Outcome
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs` (`Run`, `Copy`, `RemoveCopies`, `Tags`)
- `src/AudiobookOrganizer.Api/Services/AudioFileReader.cs` (`Read`: title, artist joined with "; ", track, `HasCover`, `ChapterCount`, `DurationMs`)
- `src/AudiobookOrganizer.Api/Models/Local/ProcessingJob.cs` (`ProcessingJobFile` statuses) and `Services/SettingsService.cs` (`Get().KeepSourceFiles`)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs` (how jobs are approved and run, where the incoming fixtures live)

## Tasks
- [x] **Source facts before the write.** In `ProcessingService.Copy`, before `TagWriter.Write`, read the source with `AudioFileReader` (read-only) and capture its size and `LastWriteTimeUtc` (`FileInfo`). Keep them per file for the rest of `Run` (a small private record; no schema change). Inject `AudioFileReader` and `SettingsService` into `ProcessingService` (both are already singletons in `Program.cs`).
- [x] **Verify after the move.** Right after `File.Move` into place, read the destination with `AudioFileReader` and compare against `BookTags` and the source facts: title equals `tags.Title` (when set); the read artist contains `tags.Artists[0]` (when any; M4B joins with ", ", MP3 with "; "); track equals `tags.Track`; `HasCover` when `tags.Cover` is set or the source had a cover; `ChapterCount` equals the source's; `|DurationMs - source| <= 1000`. On the first mismatch, delete the destination (it's this job's: it was just moved there with `overwrite: false`) and return the message, so the existing failure path marks the file and job `Failed` and removes the other copies. A read that throws counts as a mismatch the same way.
- [x] **Delete sources.** New status `ProcessingJobFile.SourceDeleted`. In `Run`, only when the loop ended with no error and `settings.Get().KeepSourceFiles` is false: for each file, if the source's size and last-write time still match the captured ones, `File.Delete` it and set `SourceDeleted`; otherwise, or when the delete throws, leave it `Copied` with the `Error` text above (log a warning). Never call `RemoveCopies` after deletion starts. The job ends `Succeeded`.
- [x] **Tests** (below). Existing success-path tests in `JobsControllerTests` that assert the sources are unchanged now need "keep source files" on, or assert deletion instead: update them, don't weaken them. Run all four checks with full backend output (TRX).
- Data: none (status is text; `SourceDeleted` is a new value).
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- `JobsControllerTests`: with keep-source off, an approved two-file book ends `Succeeded`, both destinations exist and verify, both sources are gone and both files are `SourceDeleted`.
- `JobsControllerTests`: with keep-source on, the same book ends `Succeeded`, both sources are byte-for-byte unchanged, and both files are `Copied` with no error.
- `JobsControllerTests`: a job that fails on its second file (e.g. a non-audio source, as in slice 56) deletes no source and leaves no copy, with keep-source off.
- `JobsControllerTests`: a failed read-back fails the job with the check's message and leaves no copy and every source; force it by substituting `AudioFileReader` in the test host with a fake whose read of the destination reports one chapter fewer (`AudioFileReader.Read` is virtual; see `GatedAudioFileReader`).
- `JobsControllerTests`: a source changed between its copy and the deletion step is kept with the "changed" error and the job still `Succeeded` (e.g. a fake reader that touches the first source's timestamp when it reads the second destination).

## Manual checks
- With the API running and keep-source off, approve a scanned M4B book: after `Succeeded`, the library copy plays with its chapters and cover, and the incoming file is gone.
- Turn keep-source on in Settings, approve a multi-file MP3 book: after `Succeeded`, every incoming file is still there with its original size and timestamp.

## Out of scope
- Retrying a failed job and the jobs UI with an Approve button (next slices)
- Narrator, series, series part, ASIN and other Audiobookshelf custom fields (waits on the open tag-frame question)
- What a rescan does with the groups and `AudioFile` rows of deleted sources (phase 9: "repeat scans don't duplicate processed books")
- Removing incoming folders left empty after their sources are deleted

## Notes
- File count: 3 (`Services/ProcessingService.cs`, `Models/Local/ProcessingJob.cs`, `Tests/Controllers/JobsControllerTests.cs`), plus `Tests/Services/GatedAudioFileReader.cs` or a new small fake beside it if the tests need one. Under the cap.
- Why next: slice 56's follow-up. Read-back and source removal finish phase 8's happy path; retry and the jobs UI build on the final file statuses.
- Verification runs at the destination, not on the `.partial`: `AudioFileReader` and `ChapterReader` open files through `TagLib.File.Create(path)`, which needs the audio extension the `.partial` name lacks. The copy is visible in the library for the moment between the move and the read-back; a failure deletes it. Assumption, reversible.
- Sources are deleted only after the whole job verified, so "failures leave sources intact" holds for every failed job. A source that changed or couldn't be deleted is a kept source, not a failed job: both files exist, nothing is lost, and the `Error` makes it visible. Assumption, reversible.
- The 1-second duration tolerance allows for ffprobe's MP3 duration estimate shifting when the ID3 tag grows (cover, more frames); M4B remuxes keep the duration. Assumption.
- Artist is checked by containment because the reader joins multiple values and M4B stores the joined string; the full list check waits for the custom-fields slice.
- Tests only ever delete sources in their own temp incoming folders, never `docs/fixtures/`: the implementer checks how `JobsControllerTests` sets incoming before relying on deletion.
- Conflict check: consistent with AGENTS.md file safety (a source is deleted only after its destination copy is written, tagged and read back; never overwrite), the 2026-10-18 decision (copy, tag, verify, delete; keep-source defaults to off) and slice 55's cleanup rules. No new packages, no schema change.
