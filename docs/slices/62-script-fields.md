# Slice 62: Write the Mp3tag script's book fields

**Phase:** 8, Processing
**Goal:** each copy also gets the book fields the developer's Mp3tag Audible script (`mp3tagscipt.src`) fills from values the app already stores (narrator, series, series part, movement, content group, album sort, subtitle, language, publisher, format, ISBN, ASIN, release time, Audible URL, and the iTunes media type and gapless flags on M4B), under the same names Mp3tag uses.

## Done when
- [x] `BookTags` carries `Narrators`, `Subtitle`, `Series`, `SeriesPart`, `Publisher`, `ReleaseDate`, `Language`, `Format`, `Isbn` and `Asin`, and `ProcessingService` fills them from the book's metadata (narrators by canonical name when linked, as authors are)
- [x] an MP3 copy reads back (TagLibSharp) with the frames in the mapping table below, and an M4B copy with the atoms in it; values the book lacks are left as the source had them (no empty frames or atoms are written)
- [x] derived values follow the script: CONTENTGROUP is "Series, Book #Part" (just "Series" with no part); ALBUMSORT is "Series Part - Title" with a series, else "Title - Subtitle" with a subtitle, else "Title"; the Audible URL is `https://www.audible.com/pd/<ASIN>`; SHOWMOVEMENT is 1 when there's a series; on M4B, `©mvi` is written only when the series part is a whole number
- [x] M4B copies still have no `chpl`, keep their 3 chapters and audio hash; MP3 copies keep their chapters and payload hash; sources unchanged
- [x] all four checks pass

## Mapping

| Mp3tag field | Value | MP3 (ID3v2) | M4B |
|---|---|---|---|
| COMPOSER | narrators ("; " list on MP3, ", " on M4B) | `TCOM` | `©wrt` |
| SUBTITLE | subtitle | `TIT3` | `----:com.apple.iTunes:SUBTITLE` |
| SERIES | series | `TXXX:SERIES` | `----:…:SERIES` |
| SERIES-PART | series part (text as stored) | `TXXX:SERIES-PART` | `----:…:SERIES-PART` |
| MOVEMENTNAME | series | `MVNM` | `©mvn` |
| MOVEMENT | series part | `MVIN` | `©mvi` (integer, whole numbers only) |
| SHOWMOVEMENT | 1 with a series | `TXXX:SHOWMOVEMENT` | `shwm` (integer) |
| CONTENTGROUP | "Series, Book #Part" | `TIT1` | `©grp` |
| ALBUMSORT | see Done when | `TSOA` | `soal` |
| LANGUAGE | language | `TLAN` | `----:…:LANGUAGE` |
| PUBLISHER | publisher | `TPUB` | `----:…:PUBLISHER` |
| FORMAT | format | `TXXX:FORMAT` | `----:…:FORMAT` |
| ISBN | ISBN | `TXXX:ISBN` | `----:…:ISBN` |
| ASIN | ASIN | `TXXX:ASIN` | `----:…:ASIN` |
| RELEASETIME | release date (as stored) | `TDRL` | `----:…:RELEASETIME` |
| WWWAUDIOFILE | Audible URL (with an ASIN) | `WOAF` | `----:…:WWWAUDIOFILE` |
| ITUNESMEDIATYPE | 2 (Audiobook) | none | `stik` (integer) |
| ITUNESGAPLESS | 1 | none | `pgap` (integer) |

## Read first
- `mp3tagscipt.src` (repo root): `[ParserScriptAlbum]`, where each field above is built
- `docs/slices/60-m4b-freeform-atoms-spike.md` and `Tests/Services/M4bFreeformSpikeTests.cs` (`WriteExtendedTags`: how `SetDashBox`, `©mvn`, `©mvi`, `stik`, `pgap` were written and read back)
- `src/AudiobookOrganizer.Api/Services/TagWriter.cs` (`BookTags`, `ApplyTags`) and `Services/ProcessingService.cs` (`Tags`)
- `src/AudiobookOrganizer.Api/Models/Api/MetadataDtos.cs` (`BookMetadataFieldsDto`) and `Services/AudiobookService.cs` (`GetPeople`: narrators)

## Tasks
- [x] **Book fields.** Add the fields to `BookTags` (strings and a `Narrators` list; keep the record positional or switch to `init` properties, whichever keeps call sites readable). In `ProcessingService.Tags`, fill them from `metadata.Fields` and `GetPeople(metadata).Narrators` (canonical name when linked).
- [x] **MP3 frames.** In `TagWriter`, an MP3-only step after `ApplyTags` on the file's `Id3v2` tag (`GetTag(TagTypes.Id3v2, true)`): `Composers`, `Grouping`, `AlbumSort`, `Subtitle` through the `Tag` API where TagLibSharp has them; `TLAN`, `TPUB`, `TDRL`, `MVNM`, `MVIN` as `TextInformationFrame`s; the `TXXX` values with `UserTextInformationFrame.Get(tag, name, true)`; `WOAF` as a `UrlLinkFrame`. Each is set only when it has a value.
- [x] **M4B atoms.** An M4B-only step on the `AppleTag`: `Composers`, `Grouping`, `AlbumSort` through the `Tag` API; freeform values with `SetDashBox("com.apple.iTunes", name, value)`; `©mvn` as text, `©mvi`, `shwm`, `stik`, `pgap` as integer data boxes (as in the spike). Only set when there's a value; `stik` and `pgap` always.
- [x] **Tests** (below). Run all four checks with full backend output (TRX).
- Data: none (all values are already stored).
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- `TagWriterTests`: an MP3 written with every field reads back each MP3 column of the table (including the derived CONTENTGROUP, ALBUMSORT and WOAF), keeps its chapters and payload hash.
- `TagWriterTests`: an M4B written with every field reads back each M4B column (freeform atoms through `GetDashBox`, the integer atoms), has no `chpl`, keeps its 3 chapters and audio hash.
- `TagWriterTests`: a book with no series, subtitle or ASIN writes no SERIES, MOVEMENT, CONTENTGROUP or WWWAUDIOFILE, and ALBUMSORT is the title; a series part "1.5" writes SERIES-PART "1.5" and MVIN "1.5" on MP3 but no `©mvi` on M4B.
- `JobsControllerTests`: a processed book with a series, narrator and ASIN in its metadata has COMPOSER, SERIES and ASIN in its copy (one end-to-end check that `ProcessingService` fills the new fields).

## Manual checks
- Process one MP3 and one M4B book with full Audible metadata, open both copies in Mp3tag's Extended Tags view, and compare against a file tagged with the script: each field in the table shows under the same name with the same value. Note any field Mp3tag shows under a different name (for example PUBLISHER on M4B) for a follow-up.

## Out of scope
- Fields the app doesn't store yet: COMMENT/DESCRIPTION (publisher summary), COPYRIGHT, EXPLICIT/ITUNESADVISORY, RATING, Audible category-ladder genres; they need more Audible response groups and new metadata fields (a later slice if wanted)
- The script's options (`singleAlbumArtist`, `narratorInArtist`, `singleGenre`, `delimiter`): the app keeps one behaviour (all authors as artist and album artist, narrators only in composer)
- Audible regions other than `.com`
- Extending the read-back verification beyond slice 57's checks

## Notes
- File count: 4 (`Services/TagWriter.cs`, `Services/ProcessingService.cs`, `Tests/Services/TagWriterTests.cs`, `Tests/Controllers/JobsControllerTests.cs`). Under the cap.
- Why next: the developer wants copies tagged as their Mp3tag script tags them (2026-10-20), and slice 61 made M4B writable through TagLibSharp without the `chpl` damage.
- The MP3 frame and M4B atom names are Mp3tag's usual mapping, taken from its field names in the script; the manual check against a script-tagged file confirms them. A wrong name is a one-line change.
- The script writes MOVEMENT from the Audible series sequence, which can be "1.5" or "2-3"; MP4's `©mvi` is an integer, so non-whole parts are skipped there and kept in SERIES-PART.
- Narrator stays `composer`, as decided in slice 04 and as the script does.
- Conflict check: consistent with AGENTS.md as amended in slice 61 (M4B copies have no `chpl` and are tagged by TagLibSharp), file safety (copies only, never sources) and the 2026-10-20 tagging decision. No new packages, no schema change.

## Test results (2026-10-20)

**Regressions:** one intermittent frontend failure outside this slice: `JobsPage.test.tsx > offers Retry only on a book's newest failed job and queues it again` (slice 59). It failed in 2 of about 30 full `npm test` runs (the first run of this step, then run 3 of 5; 0 of the next 23). This slice changes no frontend code. The failure output was cut off before the assertion, so the failing line isn't known; the test's `findBy*` waits after the Retry click (status, then 3 headings) are the likely place. Reported to the developer (gate 2), who accepted it on 2026-10-20.

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 383 passed, 0 failed | Was 374. TRX `slice62-tests.trx` |
| Frontend tests | 271 passed / 1 failed intermittently (accepted) | `JobsPage` Retry test: 2 of ~30 runs failed (see above) |
| New tests | 4 of 4 present, passing | `TagWriterTests` (8): every MP3 frame and M4B atom in the mapping, derived CONTENTGROUP/ALBUMSORT/WOAF, no-series/no-ASIN case, "1.5" part, chapters, audio hash and no `chpl`. `JobsControllerTests`: a processed book's copy has COMPOSER, SERIES and ASIN |
| Manual: Mp3tag Extended Tags comparison against a script-tagged file | Not run | Needs the developer and real books |

Data checks: none.

## Outcome (2026-10-20, commit a09aed7)

**Status:** Implemented
**Summary:** Every copy now carries the Mp3tag script's book fields the app stores: narrators as composer, subtitle, series, series part, movement name and number, show-movement, content group, album sort, language, publisher, format, ISBN, ASIN, release time and the Audible URL, plus `stik` and `pgap` on M4B. MP3 gets ID3v2 frames and `TXXX` values; M4B gets iTunes atoms and `----:com.apple.iTunes:` freeform atoms. Chapters, audio and sources are unchanged, and M4B copies still have no `chpl`.
**Changed:** 5 files: `Services/TagWriter.cs` (`BookTags` init properties and derived values, `ApplyMp3BookFrames`, `ApplyM4bBookAtoms`), `Services/ProcessingService.cs` (`Tags`), `Tests/Services/TagWriterTests.cs`, `Tests/Controllers/JobsControllerTests.cs`, the slice file.
**Deviations:**
- MP3 narrators are written as a TagLibSharp list, which ID3v2.3 stores as "N1/N2"; the table said a "; " list and the script writes one ", "-joined value: *debt*, confirm in Mp3tag (a one-line change).
- TagLibSharp reads `MVNM`/`MVIN` back as unknown frames, so the test decodes their bytes: *fine*.
- The v2.4-only frames (`TDRL`, `TSOA`, `MVNM`, `MVIN`) sit in the file's v2.3 tag: *debt*, check how Mp3tag shows them.
- ALBUMSORT is "Series - Title" when a series has no part (the script would give "Series  - Title"): *improvement*.
- `©mvi` only for whole numbers 0–65535; the ASIN is URL-escaped in WOAF; LANGUAGE is Audible's word ("english"), as the script does: *fine*.
- The intermittent `JobsPage` Retry test failure (slice 59, 2 of ~30 runs) is accepted by the developer: *debt*.
**Follow-ups:**
- Manual: compare an MP3 and an M4B copy with a script-tagged file in Mp3tag's Extended Tags view (names, narrator separator, v2.4 frames, `©mvi` form).
- Later, if wanted: description, copyright, explicit, rating and category-ladder genres (more Audible response groups and stored fields).
- Phase 9: harden the `JobsPage` Retry test if it keeps failing.
