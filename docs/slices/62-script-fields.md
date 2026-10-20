# Slice 62: Write the Mp3tag script's book fields

**Phase:** 8, Processing
**Goal:** each copy also gets the book fields the developer's Mp3tag Audible script (`mp3tagscipt.src`) fills from values the app already stores (narrator, series, series part, movement, content group, album sort, subtitle, language, publisher, format, ISBN, ASIN, release time, Audible URL, and the iTunes media type and gapless flags on M4B), under the same names Mp3tag uses.

## Done when
- [ ] `BookTags` carries `Narrators`, `Subtitle`, `Series`, `SeriesPart`, `Publisher`, `ReleaseDate`, `Language`, `Format`, `Isbn` and `Asin`, and `ProcessingService` fills them from the book's metadata (narrators by canonical name when linked, as authors are)
- [ ] an MP3 copy reads back (TagLibSharp) with the frames in the mapping table below, and an M4B copy with the atoms in it; values the book lacks are left as the source had them (no empty frames or atoms are written)
- [ ] derived values follow the script: CONTENTGROUP is "Series, Book #Part" (just "Series" with no part); ALBUMSORT is "Series Part - Title" with a series, else "Title - Subtitle" with a subtitle, else "Title"; the Audible URL is `https://www.audible.com/pd/<ASIN>`; SHOWMOVEMENT is 1 when there's a series; on M4B, `©mvi` is written only when the series part is a whole number
- [ ] M4B copies still have no `chpl`, keep their 3 chapters and audio hash; MP3 copies keep their chapters and payload hash; sources unchanged
- [ ] all four checks pass

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
- [ ] **Book fields.** Add the fields to `BookTags` (strings and a `Narrators` list; keep the record positional or switch to `init` properties, whichever keeps call sites readable). In `ProcessingService.Tags`, fill them from `metadata.Fields` and `GetPeople(metadata).Narrators` (canonical name when linked).
- [ ] **MP3 frames.** In `TagWriter`, an MP3-only step after `ApplyTags` on the file's `Id3v2` tag (`GetTag(TagTypes.Id3v2, true)`): `Composers`, `Grouping`, `AlbumSort`, `Subtitle` through the `Tag` API where TagLibSharp has them; `TLAN`, `TPUB`, `TDRL`, `MVNM`, `MVIN` as `TextInformationFrame`s; the `TXXX` values with `UserTextInformationFrame.Get(tag, name, true)`; `WOAF` as a `UrlLinkFrame`. Each is set only when it has a value.
- [ ] **M4B atoms.** An M4B-only step on the `AppleTag`: `Composers`, `Grouping`, `AlbumSort` through the `Tag` API; freeform values with `SetDashBox("com.apple.iTunes", name, value)`; `©mvn` as text, `©mvi`, `shwm`, `stik`, `pgap` as integer data boxes (as in the spike). Only set when there's a value; `stik` and `pgap` always.
- [ ] **Tests** (below). Run all four checks with full backend output (TRX).
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
