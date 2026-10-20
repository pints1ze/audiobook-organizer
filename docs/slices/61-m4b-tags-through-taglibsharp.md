# Slice 61: M4B copies without chpl, tagged through TagLibSharp

**Phase:** 8, Processing
**Goal:** M4B copies are remuxed by ffmpeg without the Nero `chpl` atom (chapters kept in the QuickTime chapter track) and then tagged by the same TagLibSharp code as MP3, so later slices can write freeform atoms; every tag, the cover, chapters and audio come out as in slice 56.

## Done when
- [x] `TagWriter` writes an M4B copy in two steps: one ffmpeg `-c copy` remux into the `.partial` with `-movflags disable_chpl` (audio, chapters as the chapter track, the source's tags and cover), then a TagLibSharp save of the same fields slice 56 writes (title/album, artists/album artists, genres, year, track N/M, disc, chosen cover)
- [x] a written M4B copy has no `moov/udta/chpl` box; `ChapterReader` (ffprobe) returns the source's 3 chapters with the same titles and start times; the audio streamhash is unchanged; the slice 56 tags and cover read back; the source is byte-for-byte unchanged
- [x] an M4B source that has no cover keeps none, a source cover is kept when no cover is chosen, and a chosen cover replaces it (as slice 56)
- [x] AGENTS.md's M4B rule says how M4B is written now (see Tasks); the fixtures README notes that copies carry chapters only in the chapter track
- [x] all four checks pass

## Read first
- `docs/slices/60-m4b-freeform-atoms-spike.md` (Test results and Outcome: candidate B), and `docs/fixtures/audio/README.md` ("TagLibSharp and chpl")
- `src/AudiobookOrganizer.Api/Services/TagWriter.cs` (`WriteMp3`, `WriteM4b`, `CreateNew`, the `-y` placeholder rule)
- `src/AudiobookOrganizer.Api.Tests/Services/TagWriterTests.cs` and `M4bFreeformSpikeTests.cs` (`FindPath`, `Chpl`, `ChplOnlyChapters` box helpers)
- `src/AudiobookOrganizer.Api.Tests/Services/TagRoundTripTests.cs` (`Mp4AudioHash`, the known-bug test, which stays)

## Tasks
- [x] **One tagging path.** In `TagWriter`, move the TagLibSharp field writes out of `WriteMp3` into a private `ApplyTags(TagLib.File file, BookTags tags, bool joinLists)` used by both formats. MP3 keeps lists; M4B joins artists and genres with ", " into one value (as slice 56 did through ffmpeg). Same empty-value rules: a null or empty value leaves the tag alone; a chosen cover replaces the pictures, no cover leaves them.
- [x] **M4B remux without chpl.** `WriteM4b` keeps the `CreateNew` placeholder and `-y` rule, and runs `-y -v error -i <source> -map 0:a -map 0:v? -c copy -map_chapters 0 -map_metadata 0 -disposition:v:0 attached_pic -movflags disable_chpl -f ipod <temp>`: no `-metadata` values and no cover input any more (the cover temp file goes away). Then `TagLib.File.Create(temp, <the MP4 MIME type TagLibSharp registers, e.g. "audio/mp4">, ReadStyle.Average)`, `ApplyTags(..., joinLists: true)`, `Save()`. Checked on 2026-10-20: `-movflags disable_chpl` with `-map_chapters 0` gives 3 chapters and no `chpl` on dev ffmpeg 9 and image ffmpeg 6.1.
- [x] **AGENTS.md.** Replace "Don't save M4B files with TagLibSharp: it corrupts the Nero `chpl` chapter atom (slice 04)." with: "M4B copies are remuxed by ffmpeg with `-movflags disable_chpl` (chapters live only in the QuickTime chapter track), then tagged by TagLibSharp. Never save an M4B that still has a `chpl` atom with TagLibSharp: it corrupts it (slices 04, 60)." Add a line to `docs/fixtures/audio/README.md`.
- [x] **Tests** (below). Share the spike's box helpers (`FindPath`) as `internal static` instead of copying them. Run all four checks with full backend output (TRX).
- Data: none.
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- `TagWriterTests`: an M4B written with full `BookTags` and a chosen cover has no `chpl` box, the source's 3 chapters (titles and starts) through `ChapterReader`, the source's audio streamhash, every tag and the single chosen cover; the source is unchanged. (Update the slice 56 M4B test rather than adding a parallel one.)
- `TagWriterTests`: with no chosen cover, an M4B source's embedded cover survives the remux and the TagLibSharp save; with none in the source, none is added (update the existing cases).
- `TagWriterTests`: an existing temp path still throws `TagTargetExistsException` for M4B and is left alone (existing case still passes).
- The job tests (`JobsControllerTests`) pass unchanged; the read-back's chapter-count check covers M4B copies through the chapter track.

## Manual checks
- With the API running, process a scanned M4B book: the library copy plays in the developer's usual player with its chapters and cover. If that player shows no chapters, it reads only `chpl`: record which player, since that's the trade-off the developer accepted with candidate B.

## Out of scope
- The Mp3tag script's extra fields (series, ASIN, movement, sort, subtitle, and so on) on both formats: next slice, on top of this path
- Rebuilding `chpl` from the chapter track
- MP3 changes beyond moving its field writes into `ApplyTags`

## Notes
- File count: 6 (`Services/TagWriter.cs`, `AGENTS.md`, `docs/fixtures/audio/README.md`, `Tests/Services/TagWriterTests.cs`, `Tests/Services/M4bFreeformSpikeTests.cs` for the shared helpers, `Tests/Services/TagRoundTripTests.cs` only if a helper moves). Under the cap.
- Why next: the developer chose candidate B on 2026-10-20 ("lets implement option B"). Not writing `chpl` at all (`disable_chpl`) gets B's result without the spike's whole-file byte splice, which wouldn't scale to 1 GB books, and leaves TagLibSharp nothing to damage.
- Trade-off accepted by the developer: players that read only the Nero `chpl` atom (some Android players) see no chapters in copies. Apple players, ffprobe, VLC-style players and the app's `ChapterReader` read the chapter track.
- The AGENTS.md change is part of the developer's choice of B (gate 4 resolved by that decision); it's recorded in CURRENT-STATE Decisions at review.
- TagLibSharp keeps chunk offsets valid on this save: the remux puts `moov` after `mdat`, and slice 60 showed the audio hash and chapter track survive a TagLibSharp save.
- Conflict check: consistent with AGENTS.md as amended (ffmpeg only through `FfmpegRunner` with an argument list, `-c copy`, never re-encode; sources never written; never overwrite). No new packages.
