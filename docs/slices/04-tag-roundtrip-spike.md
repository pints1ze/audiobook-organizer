# Slice 04: Tag round-trip spike (chapters and audio survive tag writes)

**Phase:** 2, Settings & scan
**Goal:** prove with automated tests that writing tags and a cover to a *copy* leaves chapters and audio unchanged. MP3 uses TagLibSharp and M4B uses an ffmpeg stream-copy remux. Add a chapter reader that the scanner will reuse, and add ffmpeg to the Docker image.

> **Rescoped 2026-10-18 at gate 3.** TagLibSharp 2.3.0 corrupts the M4B Nero `chpl` atom on every save, while the audio and QuickTime chapter track survive. The developer chose **ffmpeg/ffprobe for M4B** (write tags with `-c copy`, read chapters with ffprobe), with TagLibSharp kept for MP3 and for reading tags. Tasks 3–5 below reflect that. The original M4B TagLibSharp round-trip test is replaced, not weakened. Its finding is kept as a documented test of the known bug (see task 4).

## Done when
- [x] Small generated fixtures exist in `docs/fixtures/audio/`: `chapters.m4b` and `chapters.mp3`, each with 3 named chapters, plus a script that regenerates them
- [x] `ChapterReader` returns the 3 chapter titles and start times for both fixtures, matching what `ffprobe -show_chapters` reports (recorded in the fixtures README)
- [x] The round-trip tests pass for both formats: after writing tags and a cover to a temp copy (MP3 through TagLibSharp, M4B through ffmpeg `-c copy`), the chapters are identical, the duration is unchanged, the audio is unchanged (MP3 payload byte hash; M4B audio packet hash from ffmpeg `-f streamhash`), and the new tags and cover read back
- [x] The Docker image includes `ffmpeg` and `ffprobe` (`docker compose run --rm audiobook-organizer ffprobe -version` works)
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (TagLibSharp for MP3 and tag reading; ffmpeg/ffprobe for M4B writes and chapter reads), Layout (`Services/`, `docs/fixtures/audio/`), Environment (file safety: never tag sources in place)
- `docs/PRD.md` Rules (chapters and audio must survive tagging)
- `src/AudiobookOrganizer.Api.Tests/AudiobookOrganizer.Api.Tests.csproj` (fixtures must be copied to the test output)
- `Dockerfile` (runtime stage)
- `docs/fixtures/audio/README.md` (the chapter forms in each fixture and the ffprobe output)

## Tasks
- [x] **Fixtures.** Add `scripts/make-audio-fixtures.ps1`. It uses the developer's local `ffmpeg` (a dev tool only, not shipped in the app or the image) to generate about 30 seconds of mono low-bitrate audio as `chapters.m4b` (AAC) and `chapters.mp3` (ID3v2.3 with CHAP frames). Both get 3 chapters ("Opening", "Middle", "Ending" at 0s, 10s and 20s) and basic title and artist tags. Keep each file under 200 KB. Commit the generated files along with `docs/fixtures/audio/README.md`, which records how they were made and the `ffprobe -show_chapters` output. That output is the expected values for the tests. Also note which chapter forms the M4B carries: a Nero `chpl` atom, a QuickTime chapter text track, or both. Verify: `ffprobe` shows 3 chapters for each file.
- [x] **TagLibSharp.** Add the `TagLibSharp` package to `AudiobookOrganizer.Api.csproj`. Mark `docs/fixtures/audio/*` as content copied to the test output directory. Verify: the build is clean, with no warnings.
- [x] **Chapter reader (rework).** Keep `Services/ChapterReader.cs`, its signature and the `Chapter` record.
  - MP3: unchanged, ID3v2 CHAP frames through TagLibSharp.
  - M4B: **replace the `chpl` box walker** with `ffprobe -v error -show_chapters -of json <path>`, parsed with `System.Text.Json`. This reads both the Nero and QuickTime chapter forms. Add `Services/FfmpegRunner.cs`, which runs `ffmpeg` or `ffprobe` with an argument list (never a shell string), a timeout, captured stdout and stderr, and a clear exception on a non-zero exit or a missing executable. Executable paths come from `Configuration/ToolSettings.cs` (section `Tools`, defaults `ffmpeg` and `ffprobe` on PATH).
  - Empty-list and throw rules are unchanged.

  Verify: the existing `ChapterReaderTests` still pass. Rework the M4B empty-chapters case to use a temp copy remuxed with `-map_chapters -1`.
- [x] **Round-trip tests.**
  - **MP3:** keep the passing TagLibSharp test as it is.
  - **M4B:** replace the TagLibSharp write with an ffmpeg remux of the fixture copy into a new temp file: `-i in.m4b -i cover.jpg -map 0 -map 1 -c copy -map_chapters 0 -disposition:v:0 attached_pic`, plus `-metadata` for title, album, artist, album_artist, comment and a narrator-style custom key (use `-movflags use_metadata_tags` if needed for custom keys). The command is built in the test for now; a production M4B writer is phase 8.
  - **M4B assertions:** chapters through `ChapterReader` match; duration through TagLibSharp matches within 50 ms; the audio packet hash (`ffmpeg -i <file> -map 0:a -c copy -f streamhash -hash sha256 -`) matches; the new tags and the embedded cover read back through TagLibSharp.
  - **Known-bug test:** add one test named for the bug, e.g. `TagLibSharp_save_corrupts_m4b_chpl_atom`. It asserts the corruption still happens, so a future TagLibSharp upgrade that fixes it shows up as a failing test to revisit.

  Verify: all tests pass.
- [x] **ffmpeg in the image.** In the `Dockerfile` runtime stage, `apt-get install -y --no-install-recommends ffmpeg` and clean the apt lists. Verify: `docker compose build`, then `docker compose run --rm audiobook-organizer ffprobe -version`, then `docker compose down`.
- Data: none.
- Auth: none.
- Accessibility: none (no UI).

## Tests to add
- `Services/ChapterReaderTests.cs`: reads 3 chapters with the expected titles and start times from `chapters.m4b`.
- `Services/ChapterReaderTests.cs`: reads 3 chapters with the expected titles and start times from `chapters.mp3`.
- `Services/ChapterReaderTests.cs`: returns an empty list for a file without chapters (a copy of the fixture with chapters stripped, or a tiny generated file without them).
- `Services/TagRoundTripTests.cs`: after an ffmpeg `-c copy` tag and cover write, the M4B chapters, duration and audio packet hash are unchanged, and the new tags and cover read back.
- `Services/TagRoundTripTests.cs`: the known TagLibSharp `chpl` corruption still reproduces (guards a future upgrade).
- `Services/FfmpegRunnerTests.cs`: a missing executable path gives a clear exception; a non-zero exit surfaces stderr.
- `Services/TagRoundTripTests.cs`: the same for MP3, comparing CHAP frames and the audio payload hash.

## Manual checks
- Open the tagged temp copy of the M4B in a chapter-aware player (e.g. VLC, or Audiobookshelf if it's handy) and confirm 3 chapters and the cover show. To keep the copy, have the test write it to a known path under the git-ignored `data/` folder. Optional, but worth doing once.

## Out of scope
- Writing tags in production code (a TagWriter service for MP3 through TagLibSharp and M4B through ffmpeg): phase 8. This slice only proves the approach is safe.
- Audiobookshelf-specific custom tag names: phase 8 (open question).
- Directory settings, scanning and the dashboard: later phase 2 slices.

## Notes
- **Conflict check (after rescope):** TagLibSharp is approved. ffmpeg/ffprobe were approved by the developer on 2026-10-18 as a runtime dependency (in the Docker image) and a test dependency. Dev machines need them on PATH, and AGENTS.md is updated. The fixtures are generated silence, so there are no copyright concerns.
- **Risk:** this is the riskiest unknown in the plan, and it's why it comes first in phase 2. A failure here triggers a decision gate: ffprobe in the image, a different tag library, or a hand-written MP4 writer.
- **Size:** 5 tasks. About 13 files after the rescope: tasks 1–2 (6 files) are done and the remaining work touches about 7 (`ChapterReader.cs`, `FfmpegRunner.cs`, `ToolSettings.cs`, `appsettings.json`, `Dockerfile` and 2–3 test files). That's over the ~8 cap because of the mid-slice rescope; it's accepted rather than splitting the half-finished spike.
- **Commit:** `feat: slice 04 tag-roundtrip-spike`.
- **Remaining in phase 2, for the next writers:** directory settings (table, validation, API, settings page with a layout shell and navigation, carried from slice 03); scan (inventory table, `ScanRun`, recursive MP3/M4B discovery via TagLibSharp plus `ChapterReader`, unchanged-file skip on rescan, unreadable files recorded and not fatal); dashboard (last scan and counts, a "Scan" button).
