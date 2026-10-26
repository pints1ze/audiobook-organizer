# Slice 94: Scan unparseable files

**Phase:** 11, Parallel identification
**Goal:** an MP3 or M4B that TagLibSharp can't parse (such as a broken ID3v2 chapter table) is still scanned through ffprobe, so its book shows up instead of being silently skipped.

## Done when
- [x] when TagLibSharp throws while reading a file's tags, `AudioFileReader.Read` reads title, album, artist, album artist, composer, genre, year, track, disc, comment, duration and chapter count from one ffprobe call instead, and the scan stores the file without a `ScanError`
- [x] such a file reports `HasCover = false` (the app's cover code reads covers through TagLibSharp), and `CoverService` returns no embedded cover for it instead of a 500
- [x] a file neither TagLibSharp nor ffprobe can read still gets a `ScanError` with TagLibSharp's message, as today; a missing or unreadable file (I/O error) is not retried through ffprobe
- [x] each fallback read logs one warning naming the file and the TagLibSharp error
- [x] all four checks pass with no new warnings

## Read first
- `AGENTS.md` (External tools: call ffprobe only through `Services/FfmpegRunner.cs` with an argument list)
- `src/AudiobookOrganizer.Api/Services/AudioFileReader.cs`, `ChapterReader.cs` (opens the file with TagLibSharp first; `ReadMp4` shows the ffprobe JSON pattern) and `FfmpegRunner.cs`
- `src/AudiobookOrganizer.Api/Services/CoverService.cs` (`ReadPicture`, which catches only some TagLibSharp exceptions)
- `src/AudiobookOrganizer.Api/Services/ScanService.cs` (`ScanFile`: how a read failure becomes `ScanError`)
- `src/AudiobookOrganizer.Api.Tests/Services/AudioFileReaderTests.cs`, `ChapterReaderTests.cs` (`Fixture`, `NewReader`) and `docs/fixtures/audio/README.md` (`chapters.mp3` has a `CTOC` frame)

## Tasks
- [x] **Fallback read.** In `AudioFileReader.Read`, catch exceptions from the TagLibSharp path except `IOException`, `UnauthorizedAccessException` and `FfmpegException`, and call a new private `ReadWithFfprobe(path)`: one `ffprobe -v error -show_format -show_chapters -of json` call, parsed with `System.Text.Json` into small private records; tags matched case-insensitively (`title`, `album`, `artist`, `album_artist`, `composer`, `genre`, `date` → first 4-digit year, `track`/`disc` → the number before any `/`, `comment`), duration from `format.duration`, chapter count from `chapters`, `HasCover = false`. If the fallback throws, rethrow the original TagLibSharp exception. Log a warning (inject `ILogger<AudioFileReader>`; update `AudioFileReaderTests.NewReader` and any other construction sites).
- [x] **Cover.** In `CoverService.ReadPicture`, return null for any exception from TagLibSharp other than ones the caller must see (keep it simple: catch `Exception` there, with a one-line comment why).
- [x] **Tests** (below), then all four checks.
- Data: none.
- Auth: none (no auth in the app).
- Accessibility: none (no UI).

## Tests to add
- `Api.Tests/Services/AudioFileReaderTests.cs`: a temp copy of `chapters.mp3` whose `CTOC` frame's entry count is raised past its data (find the `CTOC` bytes and change the count byte after its element id and flags) makes `TagLib.File.Create` throw (assert that first), yet `Read` returns title "Fixture Book", artist "Fixture Author", duration ≈ 30 s, `HasCover` false.
- Same file: a temp file that's not audio at all (random bytes named `.mp3`) still throws from `Read`.
- `Api.Tests/Services/ScanServiceTests.cs` (or the grouping/scan test that copies fixtures into a temp incoming): a scan of a folder with the broken copy stores it with no `ScanError` and groups it.
- `Api.Tests` cover test (where `GET /api/groups/{id}/files/{fileId}/cover` is tested): the broken copy's embedded cover request is a 404, not a 500.

## Manual checks
- Restart the dev API and rescan the real incoming: `John Grisham - The French Illusion.mp3` appears as a book (title "The French Illusion", artist John Grisham, about 12.2 h), and the log has one fallback warning for it. Processing it is slice 95.

## Out of scope
- Processing such a file (slice 95: remux the MP3 copy with ffmpeg so TagLibSharp can tag it).
- Showing scan errors on Incoming.
- Reading the embedded cover through ffmpeg.

## Notes
- Developer decision, 2026-10-26: scan and process (94 + 95).
- The real file is never used in tests; the broken copy is built from the fixture in a temp directory.
- `ChapterReader.Read` opens the file with TagLibSharp too, so the fallback must catch around both the tag read and the chapter read, and count chapters from ffprobe.
- Files touched: `AudioFileReader.cs`, `CoverService.cs`, `Program.cs` only if the reader's registration needs it, `AudioFileReaderTests.cs`, a scan test file, a cover test file, plus construction sites of `AudioFileReader` in tests (`GatedAudioFileReader.cs`, `AdjustingAudioFileReader.cs`): about 8.

## Test results (2026-10-26)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | API and test project 0 warnings; `-p:OutDir=<scratchpad>` |
| Typecheck | Passed | |
| Backend tests | 450 passed, 0 failed | 2 full runs |
| Frontend tests | 377 passed, 0 failed | 22 files |
| New tests | 4 of 4 present, passing | `AudioFileReaderTests`: broken-CTOC copy read through ffprobe (TagLib throws first; title, artist, ~30 s, 3 chapters, no cover, one warning); random bytes still throw. `ScanServiceTests`: the broken copy is stored without `ScanError` and grouped. `GroupCoverControllerTests`: its file cover is a 404, not a 500 |
| Manual: rescan the real incoming, the Grisham MP3 appears as a book | Not run | needs the dev API restarted |

## Outcome (2026-10-26, commit f5d5d88)

**Status:** Implemented
**Summary:** When TagLibSharp throws on a file (other than I/O, access or ffmpeg errors), `AudioFileReader.Read` reads its tags, duration and chapter count from one ffprobe JSON call, reports no cover and logs one warning; if ffprobe fails too, the TagLibSharp error is recorded as before. `CoverService.ReadPicture` returns no embedded cover for any TagLibSharp failure instead of a 500.
**Changed:** 7 code files (`AudioFileReader.cs`, `CoverService.cs`, `AudioFileReaderTests.cs`, `ScanServiceTests.cs`, `GroupCoverControllerTests.cs`, `AdjustingAudioFileReader.cs`, `GatedAudioFileReader.cs`)
**Done when:** all 5 met; manual check not run.
**Deviations:**
- `TryReadWithFfprobe` logs the ffprobe error at Debug and returns null so the original exception rethrows with its stack: improvement.
- An extra assertion on the single warning, with a small in-file `ListLogger`: improvement.
- No test that an I/O error skips the ffprobe retry (the exception filter enforces it): fine.
**Follow-ups:**
- Slice 95: process such files (remux the MP3 copy with ffmpeg before TagLibSharp tags it).
