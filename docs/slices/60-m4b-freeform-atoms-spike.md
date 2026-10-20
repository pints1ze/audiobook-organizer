# Slice 60: M4B freeform atoms spike

**Phase:** 8, Processing
**Goal:** find out, with automated tests on a copy of the M4B fixture, whether the app can write the developer's Mp3tag fields that M4B keeps in freeform `----:com.apple.iTunes:` atoms (and the iTunes atoms ffmpeg can't write) without breaking chapters, audio, cover or the standard tags; record which approach works so the developer can pick one.

## Done when
- [x] the exact `chpl` damage from a TagLibSharp save is described in the fixtures README (what changes in the box: size, entry count, times or titles, and whether ffprobe still reports 3 chapters and from which form)
- [x] each candidate below has a test on a temp copy of `docs/fixtures/audio/chapters.m4b` that first runs the slice 56 ffmpeg remux (standard tags + cover), then the candidate, and asserts or records: freeform atoms read back through TagLibSharp; `©mvn`, `©mvi`, `stik`, `pgap`, `rtng` read back; the 3 chapters through `ChapterReader` (ffprobe) **and** the Nero `chpl` box itself are unchanged against the remuxed copy; the audio streamhash is unchanged; the cover and the standard tags from the remux still read back
- [ ] the result of each candidate is in the slice's Test results as works / fails (with how), and a recommendation is written under Outcome; nothing in `TagWriter` or production code changes
- [x] all four checks pass (a candidate that fails is asserted as failing, like slice 04's known-bug test, so the suite stays green)

## Read first
- `docs/slices/04-tag-roundtrip-spike.md` (Outcome: TagLibSharp 2.3.0 corrupts `chpl`, the QuickTime chapter track survives; `use_metadata_tags` writes `mdta` keys and drops the cover)
- `src/AudiobookOrganizer.Api.Tests/Services/TagRoundTripTests.cs` (`TagLibSharp_save_corrupts_m4b_chpl_atom`, `Mp4AudioHash`, `Snapshot`, the M4B remux)
- `src/AudiobookOrganizer.Api/Services/TagWriter.cs` (`WriteM4b`: the remux the candidates start from)
- `mp3tagscipt.src` (repo root): the developer's Mp3tag Audible script, the field list to write
- `docs/fixtures/audio/README.md`

## Tasks
- [x] **Characterize the damage.** In a new `Tests/Services/M4bFreeformSpikeTests.cs`, a small test-only box reader (walk `moov/udta/chpl`, return its raw bytes; walk `moov/udta/meta/ilst`) to compare `chpl` before and after a TagLibSharp save that adds one freeform atom. Write what changed into `docs/fixtures/audio/README.md` (a short "TagLibSharp and chpl" section).
- [x] **Candidate A: TagLibSharp save, then restore `chpl`.** After the remux, write the fields with TagLibSharp (`TagLib.Mpeg4.AppleTag`: `SetDashBox("com.apple.iTunes", name, value)` for the freeform ones, and the `©mvn`/`©mvi`/`stik`/`pgap`/`rtng` boxes), save, then overwrite the damaged `chpl` box with the remuxed copy's `chpl` bytes. `chpl` holds only times and titles, no file offsets, so this is safe only if the box size and position work out: if the restored box is a different size than the damaged one, rewrite the parent `udta`/`moov` sizes, or report that the candidate needs more than a byte copy. Assert as in Done when.
- [x] **Candidate B: drop `chpl`, keep the QuickTime chapter track.** Same TagLibSharp write, with the `chpl` box removed (or never written) so only the QuickTime chapter track carries chapters. Record whether ffprobe still reads 3 chapters and whether TagLibSharp and the box reader see any chapter data. This changes which players see chapters (Apple players read the track; some Android players read only `chpl`), so it's recorded, not recommended by default.
- [x] **Candidate C: ffmpeg `-movflags use_metadata_tags` again.** Re-check slice 04's finding on dev ffmpeg 9: do the custom keys come out as `mdta` keys or `----` atoms, can TagLibSharp read them, and is the cover kept? One test, asserting what actually happens.
- [ ] **Record.** Test results per candidate (works / fails, and how), then all four checks with full backend output (TRX). If image ffmpeg 6.1 behaves differently for the chosen candidate, that's a manual check below.
- Data: none.
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `M4bFreeformSpikeTests`: a TagLibSharp save of one freeform atom changes the remuxed copy's `chpl` box (characterization; the existing known-bug test already proves it breaks).
- `M4bFreeformSpikeTests` (A): after the TagLibSharp write and the `chpl` restore, `SERIES`, `SERIES-PART`, `ASIN`, `SUBTITLE` freeform atoms and `©mvn`/`©mvi`/`stik` read back, `chpl` bytes equal the remuxed copy's, `ChapterReader` returns the 3 chapters, the audio streamhash and cover are unchanged.
- `M4bFreeformSpikeTests` (B): with `chpl` removed, the freeform atoms read back and ffprobe's chapter count is recorded (asserted as whatever it is).
- `M4bFreeformSpikeTests` (C): `use_metadata_tags` output asserted as observed (key form, TagLibSharp readability, cover present or not).

## Manual checks
- Open candidate A's (or the recommended candidate's) output in Mp3tag's Extended Tags view: SERIES, SERIES-PART, ASIN, SUBTITLE, MOVEMENTNAME, MOVEMENT and ITUNESMEDIATYPE show with the written values, and the cover shows. Keep the file by having the test write to the git-ignored `data/` folder once.
- Open the same file in a chapter-aware player (VLC, or the developer's usual player): 3 chapters named Opening, Middle, Ending.
- Inside the image (`docker compose run --rm --entrypoint ffprobe audiobook-organizer -show_chapters …`): ffprobe 6.1 reads the 3 chapters from candidate A's file.

## Out of scope
- Changing `TagWriter`, the job, or what the app stores: the next slice, after the developer picks a candidate
- Fetching the extra Audible fields the script uses that the app doesn't store (description, copyright, rating, category ladders)
- MP3 custom frames (TXXX via TagLibSharp is known to work; it goes into the writer slice)
- New tools or packages (AtomicParsley, mp4v2, a different tag library): if A–C all fail, that's a gate-4 question for the developer, not something to add here

## Notes
- File count: 3 (new `Tests/Services/M4bFreeformSpikeTests.cs`, `docs/fixtures/audio/README.md`, possibly `Tests/Services/TagRoundTripTests.cs` if helpers are shared). Under the cap.
- Why next: the developer wants the app to tag files like their Mp3tag Audible script (2026-10-20); most of the script's book fields live in M4B freeform atoms, which ffmpeg can't write and TagLibSharp writes only by damaging `chpl`. Whether that damage can be repaired decides the writer's design.
- The developer's intent (2026-10-20): Audiobookshelf compatibility isn't the goal; files should carry full tags for the developer's own later renaming and regrouping. The PLAN.md done-when wording ("Audiobookshelf-compatible custom fields") is updated when this slice is reviewed, with the developer's OK.
- The test files must be temp copies; the fixture in `docs/fixtures/audio/` is never written.
- This is a spike: its result is a gate (4, needs decision). The loop stops after its review for the developer to choose.
- Conflict check: consistent with AGENTS.md (no M4B saves with TagLibSharp in production; this is test-only investigation; no new packages; ffmpeg only through `FfmpegRunner` or the test helpers that already call it). The narrator-as-composer decision stands (the script does the same).
