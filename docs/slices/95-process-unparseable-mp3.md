# Slice 95: Process unparseable MP3

**Phase:** 11, Parallel identification
**Goal:** an MP3 whose tags TagLibSharp can't parse (such as a broken ID3v2 chapter table) can be approved and processed: its copy is remuxed by ffmpeg into clean tags first, then tagged and checked as usual.

## Done when
- [x] `TagWriter.Write` for an MP3 whose byte copy TagLibSharp can't open (any exception except `IOException`, `UnauthorizedAccessException` or `FfmpegException`) replaces that copy with an ffmpeg remux of the source (`-c copy`, audio plus any attached picture, the source's tags and chapters, ID3v2.3) and tags the remuxed copy with TagLibSharp exactly as other MP3 copies
- [x] the remuxed copy passes the existing read-back (title, first artist, track, cover when chosen, chapter count equal to the source's as the scan read it, duration ±1 s); the byte-size check applies only to the plain byte copy
- [x] MP3s TagLibSharp can read keep today's path (byte copy, no ffmpeg); M4B is unchanged
- [x] a job for a book containing such an MP3 succeeds end to end on a temp library, and the source is deleted only after the copy verified (keep-source off)
- [x] all four checks pass with no new warnings

## Read first
- `AGENTS.md` (External tools: ffmpeg only through `Services/FfmpegRunner.cs` with an argument list; never re-encode)
- `docs/slices/94-scan-unparseable-files.md` and its Outcome (the scan fallback, its exception filter, the broken-CTOC test copy)
- `src/AudiobookOrganizer.Api/Services/TagWriter.cs` (`Write`, `WriteMp3`, `WriteM4b`: the remux and `CreateNew` placeholder pattern)
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs` (the copy, `.partial`, read-back `Verify` and source deletion)
- `src/AudiobookOrganizer.Api.Tests/Services/TagWriterTests.cs`, `AudioFileReaderTests.cs` (`WriteBrokenMp3`) and `Controllers/JobsControllerTests.cs` (end-to-end job tests)

## Tasks
- [x] **Remux fallback.** In `TagWriter.WriteMp3`, after the byte copy and size check, try `TagLib.File.Create`; on a parse failure (filter as above) dispose, delete the copy, recreate the placeholder with `CreateNew` and run `ffmpeg -y -v error -i <source> -map 0:a -map 0:v? -c copy -map_metadata 0 -id3v2_version 3 -f mp3 <temp>`, then open that with TagLibSharp and apply the tags as before. If the remuxed copy still can't be opened, let the error propagate (the job fails with it). Update the class summary.
- [x] **Shared test helper.** Move `WriteBrokenMp3` from `AudioFileReaderTests` to an `internal static` helper both test classes can use (for example on `AudioFileReaderTests`, or a small `BrokenMp3.cs` in the tests' `Services/` folder).
- [x] **Tests** (below), then all four checks.
- Data: none.
- Auth: none (no auth in the app).
- Accessibility: none (no UI).

## Tests to add
- `Api.Tests/Services/TagWriterTests.cs`: writing the broken-CTOC copy produces a file TagLibSharp opens, with the new title, artist and track, the fixture's 3 chapters (`ChapterReader`) and ~30 s of audio; the source file is unchanged (same bytes).
- Same file: a normal `chapters.mp3` still takes the byte-copy path (the copy's audio frames are byte-identical to the source's after the tag, as the existing payload test checks), so ffmpeg isn't involved.
- `Api.Tests/Controllers/JobsControllerTests.cs`: a scanned book made of the broken copy is approved and its job ends `Succeeded`, with the destination file present and tagged and the source deleted.

## Manual checks
- Restart the dev API, rescan, match and approve "The French Illusion" (John Grisham): the job succeeds, the library file plays in a player, shows its chapters and the new tags, and the incoming file is gone.

## Out of scope
- Unparseable M4B files (they're remuxed already; a failure there still fails the job).
- Reading the source's embedded cover through ffmpeg for the cover picker.
- Repairing source files: sources are never modified.

## Notes
- Developer decision, 2026-10-26: scan and process (94 + 95).
- `AGENTS.md` says MP3 copies are tagged by TagLibSharp and keep their chapters; both still hold. Its External tools line lists only the M4B remux, so this slice proposes adding "and the remux of MP3s TagLibSharp can't parse" there (gate 4: the developer edits AGENTS.md).
- Risk: ffmpeg rewrites the MP3's Xing header on remux; the ±1 s duration read-back catches a real difference and fails the job rather than shipping a bad copy.
- Files touched: `TagWriter.cs`, `TagWriterTests.cs`, `AudioFileReaderTests.cs`, `JobsControllerTests.cs`, possibly a new `BrokenMp3.cs` helper: 5.
