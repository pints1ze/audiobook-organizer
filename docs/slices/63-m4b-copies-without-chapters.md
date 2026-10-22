# Slice 63: M4B copies without chapters

**Phase:** 9, Hardening & release
**Goal:** M4B copies are written with no chapters and pass their read-back, so a real M4B book processes; MP3 copies keep their chapters.

## Done when
- [x] `TagWriter` remuxes M4B with `-map_chapters -1`: the copy has no chapters (ffprobe finds none) and no `chpl` atom, and keeps its audio, cover and tags
- [x] `ProcessingService.Verify` skips the chapter-count check for M4B copies; MP3 copies still fail when their chapter count differs from the source
- [x] an M4B book with chapters goes through a job to `Succeeded`, and its copy has 0 chapters
- [x] AGENTS.md's M4B rule says M4B copies carry no chapters
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docs/CURRENT-STATE.md`, Decisions: 2026-10-21 (developer) "M4B copies carry no chapters (option A)" and 2026-10-20 (slice 61)
- `src/AudiobookOrganizer.Api/Services/TagWriter.cs` (`WriteM4b`, lines ~100–119, and the class summary)
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs` (`Verify`, lines ~240–268)
- `src/AudiobookOrganizer.Api.Tests/Services/TagWriterTests.cs` (the two M4B tests at ~129 and ~307 that assert chapters are kept)
- `src/AudiobookOrganizer.Api.Tests/Controllers/JobsControllerTests.cs` (setup at ~35–45, `ScanTwoBooks`, `ReadBack`, and `A_failed_read_back_fails_the_job_naming_the_check_and_leaves_no_copy_and_every_source` at ~468)

## Tasks
- [x] Change the two M4B tests in `TagWriterTests.cs` to expect no chapters: `ChapterReaderTests.NewReader().Read(temp)` is empty, the `chpl` atom is still absent, audio hash, cover and tags unchanged. The test names drop "keeps_chapters" for "drops_chapters". If `TagRoundTripTests.Snapshot` includes chapters, compare everything except them. Watch both fail.
- [x] In `TagWriter.WriteM4b`, replace `-map_chapters 0` with `-map_chapters -1`; keep `-movflags disable_chpl` (harmless, and guards against a muxer default writing `chpl`). Update the class summary and the remux comment. The two tests go green.
- [x] In `JobsControllerTests.cs`, add a test that processes an M4B book (copy `ChapterReaderTests.Fixture("chapters.m4b")` into its own book folder under incoming, scan, approve, wait): the job is `Succeeded`, the copy exists with 0 chapters, and the source is deleted (keep-source off). Watch it fail on the chapter check.
- [x] In `ProcessingService.Verify`, skip the chapter check when the destination's extension is `.m4b` (ignore case), with a one-line comment pointing at the 2026-10-21 decision. The new test goes green; `A_failed_read_back_...` (MP3, chapter mismatch) still passes unchanged.
- [x] Update AGENTS.md's External tools line: M4B copies are remuxed by ffmpeg with no chapters (`-map_chapters -1`, `-movflags disable_chpl`), then tagged by TagLibSharp; keep the "never save an M4B that still has a `chpl` atom" warning. Run all four checks.
- Data: none
- Auth: none
- Accessibility: none

## Tests to add
- `TagWriterTests`: the M4B full-tags test and the M4B script-atoms test now assert the copy has no chapters and no `chpl`, with audio, cover and tags intact (changed tests, must be seen to fail first).
- `JobsControllerTests`: an M4B book with chapters processes to `Succeeded`, its copy has 0 chapters and its source is deleted.
- The existing MP3 chapter-mismatch test (`A_failed_read_back_...`) stays as is and passes, proving MP3 chapters are still checked.

## Manual checks
- Process the real M4B book that failed (27 chapters, kept 6) from a copy in a temp incoming folder: the job succeeds and the copy plays from start to end with no chapter list.
- Open the M4B copy in Mp3tag: tags and cover are as before slice 63.

## Out of scope
- Writing chapters back into M4B copies by any other route (rejected by the developer for now).
- Any change to MP3 chapters, `ChapterReader`, or how scans record a source's chapter count.
- The UI: nothing on the Jobs or Preview pages mentions chapters.

## Notes
- Conflict check: matches the 2026-10-21 developer decision (option A) and PLAN.md phase 9's first item. The AGENTS.md change was approved with that decision, so it isn't a new gate-4 item.
- Changing the two M4B TagWriter tests is a behavior change the developer chose, not a weakened test.
- Files touched: `TagWriter.cs`, `ProcessingService.cs`, `TagWriterTests.cs`, `JobsControllerTests.cs`, `AGENTS.md`, this slice file (6).
- Risk: dev ffmpeg is 9.0 and the image has 6.1; `-map_chapters -1` exists in both.
- Remaining phase 9 work for later slices: backend suite inside the Docker image, end-to-end fixture scan → process, interrupted-job restart, repeat scans after processing (deleted sources, empty incoming folders), Docker docs, and the carried-in items in PLAN.md.
