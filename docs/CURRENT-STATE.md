# Current state: AudiobookOrganizer

_Updated: slice 09 review, 2026-10-18, commit 15485d5_

## Branch
`phase/2-settings-scan` (phase done; PR to `main` open, waiting for the developer to verify and merge)

## Where things stand
- **Locally:** the API runs on port 5070 (`scripts/start-api.ps1`) with SQLite at `<repo>/data/audiobooks.db`. The React client runs on port 5173 (`scripts/start-client.ps1`) and shows the API and database status.
- **Docker:** `docker compose up --build` serves both on port 8080, with `/data`, `/incoming` and `/library` mounted from `./docker/` by default.
- **Tag safety:** proven by tests. MP3 writes go through TagLibSharp and M4B writes through an ffmpeg `-c copy` remux; chapters and audio survive both. `ChapterReader` reads chapters from both formats. The image ships ffmpeg/ffprobe 6.1.
- **Settings API:** `GET`/`PUT /api/settings` stores the validated incoming and library paths and the keep-source toggle in SQLite (`ApplicationSetting`). The Settings page (`/settings`) edits them, inside a layout with nav, a skip link and a not-found page.
- **Scan inventory:** `ScanService` records every MP3 and M4B under incoming in `AudioFile`, with a `ScanRun` per scan. It skips unchanged files and records unreadable ones, retrying them on the next scan. `POST /api/scans` runs one background scan at a time, `GET /api/scans/latest` shows progress, and `GET /api/dashboard` returns the paths and counts. The dashboard shows the directories, last scan and counts, with a "Scan incoming" button and live progress. The whole flow is verified on Windows dev and in Docker (Linux).
- **Tests:** both test projects exist and all four checks pass.

## Phase
Phase 2, Settings & scan: done and awaiting merge. Phase 3, Grouping, starts after the merge.

## Last slice / next slice
- Last: 09 dashboard-ui, Implemented
- Next: 10, the first slice of phase 3 (not written yet)

## Open questions
- Exact ID3 frames / MP4 atoms Audiobookshelf reads for narrator, series, series part and ASIN. ffmpeg cannot write freeform M4B atoms alongside a cover, so the narrator is currently written as `composer` (phase 8, raised again in slice 04)
- Audible catalog API rate limits and terms (phase 5)
- Edition handling: multiple narrators, abridged vs unabridged, regional releases (phase 5)
- Genre source and taxonomy (deferred; manual in v1)

## Decisions
- 2026-10-18: Docker (Linux), single user, no auth. Trusted LAN; overview.md's deployment wins over AGENTS.md's IIS/Entra template.
- 2026-10-18: SQLite + Dapper, not PostgreSQL. One user, one file on a mounted volume.
- 2026-10-18: TagLibSharp handles tag reading and MP3 writes.
- 2026-10-18 (slice 04, gate 3): TagLibSharp 2.3.0 corrupts the M4B `chpl` chapter atom when it saves. The developer chose ffmpeg/ffprobe, installed in the image, for M4B tag and cover writes (`-c copy`) and for M4B chapter reads. AGENTS.md is updated.
- 2026-10-18: Audible catalog search is the primary provider, with Open Library as fallback. Matches are never auto-applied.
- 2026-10-18: Sources are copied, the copy is tagged and verified, then the source is deleted. The "keep source files" setting defaults to off.
- 2026-10-18: Template syntax is `%token%`, `%token:N%` padding and `[optional]` sections only.
- 2026-10-18: Covers can come from the embedded image, a provider download or an upload. The chosen file is embedded in every output file.
- 2026-10-18: Custom tags follow Audiobookshelf conventions.
- 2026-10-18: Genres are entered by hand in v1. Provider genres come later.
- 2026-10-18 (slice 01): `.gitignore` uses `*.db*` patterns instead of `data/`, because case-insensitive git would also ignore the `Data/` source folders.
- 2026-10-18 (slice 01): There's no HTTPS, auth middleware or OpenAPI in the API.
- 2026-10-18 (slice 02): The typecheck command is `npx tsc -b --noEmit`, because Vite's root tsconfig only holds references. AGENTS.md was updated with the developer's OK.
- 2026-10-18 (slice 03): The dev database lives at `<repo>/data/` (`../../data/audiobooks.db`) and the container uses `/data/audiobooks.db`. Unknown `/api/*` paths return 404; other paths get the SPA shell.
- 2026-10-18 (slice 04): M4B chapters are read through ffprobe JSON, and fixtures are generated silence (`scripts/make-audio-fixtures.ps1`). A known-bug test guards the TagLibSharp `chpl` corruption.
- 2026-10-18 (slice 05): Settings are key/value rows in `ApplicationSetting`. The schema is `CREATE TABLE IF NOT EXISTS` in `DatabaseInitializer`, with no migration framework. Validation never writes into incoming.
- 2026-10-18 (slice 07): Duration comes from ffprobe, because TagLibSharp misreads MPEG-2 MP3 duration. Files are unchanged when size and mtime match. Rows for missing files are kept and counted.
- 2026-10-18 (slice 08): Only one scan runs at a time (409 otherwise). Runs still `Running` at startup are marked `Failed`. Dashboard counts come from the latest completed run.

## Verification
Full suite: backend 59 passed / 0 failed, frontend 40 passed / 0 failed. Typecheck and build clean. No accepted failures. The tests run against dev ffmpeg 9.0; the image has 6.1, and the M4B remux was checked by hand there.
