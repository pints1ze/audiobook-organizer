# Slice 56: Write tags and the cover into each copy

**Phase:** 8, Processing
**Goal:** while a job copies a file, the copy gets the book's standard tags (title, album, artists, album artists, genres, year, track N of M, disc) and the chosen cover before it moves into place; chapters and audio survive, and the source is never written.

## Done when
- [x] a processed MP3 reads back (TagLibSharp) with title and album = the book title, performers and album artists = the authors (canonical names where linked), genres (canonical where linked), year from the release date, track = its position with the file count as total, its scanned disc, and the chosen cover as the only front cover
- [x] a processed M4B reads back with the same values, keeps its chapters (`ChapterReader`) and audio packets (ffmpeg streamhash) unchanged, and keeps its embedded cover when the book has no chosen cover
- [x] values the book doesn't have are left as the source had them (e.g. no genres → the source's genre stays); unrelated tags in the source survive
- [x] a job whose group no longer exists fails with "The book is no longer in the library."; a tag write that fails (e.g. ffmpeg error) fails the file and job with the message and removes the job's copies as in slice 55
- [x] sources are byte-for-byte unchanged; all four checks pass

## Read first
- `docs/slices/55-copy-runner.md` and its Outcome
- `src/AudiobookOrganizer.Api.Tests/Services/TagRoundTripTests.cs` (the proven MP3 and M4B write paths, the `chpl` warning, `Mp4AudioHash`, `Snapshot`)
- `src/AudiobookOrganizer.Api/Services/ProcessingService.cs` (`Run`, `Copy`, `RemoveCopies`) and `Services/FfmpegRunner.cs`
- `src/AudiobookOrganizer.Api/Services/NamingPreviewService.cs` (`Book`: metadata, `GetPeople`, canonical names) and `Services/Naming/BookTokens.cs` (year)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`GetMetadata`, `GetPeople`; the genres equivalent) and `Data/AudiobookRepository.cs` (`GetCoverImage`)

## Tasks
- [x] **Tag writer.** New `Services/TagWriter.cs` with a `BookTags` record (`Title`, `Artists`, `Genres`, `Year`, `Track`, `TrackCount`, `Disc`, `Cover` as bytes + MIME type or null) and `Write(string sourcePath, string tempPath, BookTags tags)`, which creates `tempPath` (failing if it exists) as a tagged copy:
  - **MP3:** stream-copy the source into `tempPath` with `FileMode.CreateNew` (as slice 55 does), check the size, then open it with `TagLib.File.Create(tempPath, "audio/mpeg", ReadStyle.Average)` (the `.partial` name has no extension) and set the fields. A null or empty value leaves that tag alone. With a cover, `Pictures` becomes just that front cover; without one, pictures are untouched. `Save()`.
  - **M4B:** one ffmpeg call through `FfmpegRunner`, never TagLibSharp saves (AGENTS.md): `-n -v error -i <source> [-i <cover file>] -map 0:a [-map 1 | -map 0:v?] -c copy -map_chapters 0 -map_metadata 0 -disposition:v:0 attached_pic -metadata title=… album=… artist=… album_artist=… genre=… date=… track=N/M disc=… -f ipod <tempPath>`. The cover goes to a temp file next to `tempPath` (`.cover.jpg`/`.png`), deleted afterwards. `-n` and the earlier existence check mean nothing is ever overwritten.
- [x] **Use it in the job.** `ProcessingService.Run`: before the files, load the book through `AudiobookService` by `job.GroupId` (null or gone → fail the job with the message above, no files touched) and the chosen cover through `AudiobookRepository.GetCoverImage`. Build each file's `BookTags`: artists = every author resolved to its canonical name when linked, genres likewise, year as in `BookTokens`, track = position, total = file count, disc = the scanned disc. `Copy` calls `TagWriter.Write` instead of its own stream copy; the `.partial` ownership, move into place and cleanup stay as they are. Register `TagWriter` in `Program.cs`.
- [x] **Tests** (below). Run all four checks with full backend output (TRX).
- Data: none.
- Auth: none (single user, no login).
- Accessibility: none (API only).

## Tests to add
- New `Services/TagWriterTests.cs`: an MP3 fixture written with full `BookTags` and a cover reads back every value and the single cover; chapters and the MP3 payload hash match the source (reuse the round-trip helpers, making them `internal static` if needed); the source is unchanged.
- `TagWriterTests`: an M4B fixture written with full `BookTags` reads back every value, keeps its 3 chapters and audio hash; with no cover, the source's embedded cover (if the fixture has one; otherwise assert no picture is added) is kept.
- `TagWriterTests`: empty genres and a null year leave the source's genre and year as they were; an existing `tempPath` throws and isn't changed.
- `JobsControllerTests`: an approved two-file book with a chosen uploaded cover ends `Succeeded`, and each destination reads back the book title, author, track 1/2 and 2/2, and the cover; the sources are unchanged.
- `JobsControllerTests`: a job whose group was deleted after approval (e.g. merged away) fails with "The book is no longer in the library." and writes nothing.

## Manual checks
- With the API running, approve a scanned M4B book with a chosen cover: after `Succeeded`, open the library copy in a player or Audiobookshelf: title, author, cover and chapters show; the incoming file is unchanged (size and timestamp).
- Same for a multi-file MP3 book: tracks read 1/N…N/N in book order.

## Out of scope
- Narrator, series, series part, ASIN and other Audiobookshelf custom fields: they depend on the open tag-frame question and come next, once it's answered
- Read-back verification as a job step, deleting sources (next slices)
- Writing tags on the M4B through anything other than ffmpeg

## Notes
- File count: 6 (new `Services/TagWriter.cs`, `ProcessingService.cs`, `Program.cs`, new `Tests/Services/TagWriterTests.cs`, `Tests/Services/TagRoundTripTests.cs` if its helpers are shared, `Tests/Controllers/JobsControllerTests.cs`). Under the cap.
- Why next: slice 55's follow-up. The tagged copy is what verification, source deletion and retry act on.
- The M4B copy is a remux, so its size differs from the source: the slice 55 size check stays for MP3 only; slice 57+ verifies by reading back.
- Tags come from the book's current metadata at run time (not the approval snapshot); only the destinations are snapshotted. The job runs seconds after approval, so the two rarely differ.
- `-map_metadata 0` keeps the source's other tags; the explicit `-metadata` values override them. `-f ipod` is the muxer ffmpeg picks for `.m4b`, named because the `.partial` name has no extension.
- Conflict check: consistent with AGENTS.md (M4B via ffmpeg `-c copy`, never TagLibSharp saves; ffmpeg only through `FfmpegRunner` with an argument list; sources never tagged in place), the cover decision (chosen cover embedded in every file) and the developer's track-number decision. The narrator-as-composer question stays open. No new packages.
