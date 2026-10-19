# Current state: AudiobookOrganizer

_Updated: slice 36 review, 2026-10-19, commit 605c4d2_

## Branch
`phase/5-metadata` (off `main` after the phase 4 merge, PR #4)

## Where things stand
- **Locally:** the API runs on port 5070 (`scripts/start-api.ps1`) with SQLite at `<repo>/data/audiobooks.db`. The React client runs on port 5173 (`scripts/start-client.ps1`) and shows the API and database status.
- **Docker:** `docker compose up --build` serves both on port 8080, with `/data`, `/incoming` and `/library` mounted from `./docker/` by default.
- **Tag safety:** proven by tests. MP3 writes go through TagLibSharp and M4B writes through an ffmpeg `-c copy` remux; chapters and audio survive both. `ChapterReader` reads chapters from both formats. The image ships ffmpeg/ffprobe 6.1.
- **Settings API:** `GET`/`PUT /api/settings` stores the validated incoming and library paths and the keep-source toggle in SQLite (`ApplicationSetting`). The Settings page (`/settings`) edits them, inside a layout with nav, a skip link and a not-found page.
- **Scan inventory:** `ScanService` records every MP3 and M4B under incoming in `AudioFile`, with a `ScanRun` per scan. It skips unchanged files and records unreadable ones, retrying them on the next scan. `POST /api/scans` runs one background scan at a time, `GET /api/scans/latest` shows progress, and `GET /api/dashboard` returns the paths and counts. The dashboard shows the directories, last scan and counts, with a "Scan incoming" button and live progress. The whole flow is verified on Windows dev and in Docker (Linux).
- **Grouping engine:** the pure `GroupingEngine.Group` proposes book groups from scanned file facts, with confidence, evidence and an estimated title and author; scenarios A–E pass. Every completed scan regroups the files it saw into `FileGroup` and `FileGroupFile` (replacing all groups), and `GET /api/groups` lists the groups (least confident first) and the unresolved files. The Incoming page (`/incoming`) shows them with confidence and evidence.
- **Corrections:** `PUT /api/groups/{id}` sets a group's title and author and marks it `UserCorrected`. Rescans keep corrected groups and their files untouched and regroup only the rest. The Incoming page edits title and author in place and marks corrected books "Edited" (slice 14). `GET /api/groups/{id}` lists a group's files in order and `PUT /api/groups/{id}/order` reorders them (slice 15). Each Incoming row has a "Files" disclosure with keyboard-accessible Move up and Move down (slice 16). `POST /api/groups/{id}/files` moves files into a group and `POST /api/groups` creates a group from files; together they give move, merge and split (slice 17). Each Files list has checkboxes and a "Move selected to" bar that moves files to another book or a new book (slice 18). The Unresolved section has the same selection and move bar (slice 19, shared `MoveBar`). `POST /api/groups/excluded` and `POST /api/groups/unresolved` set a file's `UserState`, and `GET /api/groups` lists `excluded` (slice 20). The move bar also offers "Unresolved files" and "Excluded files", and an Excluded section sends files back (slice 21).
- **Metadata search:** `GET /api/metadata/search?keywords=` searches Audible's catalog through `AudibleClient`, a typed `HttpClient` behind `IMetadataProvider`. It returns normalized candidates (ASIN, title, authors, narrators, series, publisher, release date, language, runtime, format, cover URL) (slice 27). `MetadataSearchService` falls back to `OpenLibraryClient` when Audible fails or finds nothing; ISBN keywords search Open Library by ISBN. The response is `{ candidates, providerErrors }`: 400 for blank keywords, 502 only when every provider failed (slice 28). Tests use real responses in `docs/fixtures/audible/` and `docs/fixtures/openlibrary/` through `FakeHttpMessageHandler`. Each Incoming book links ("Find metadata") to `/books/:groupId`, which shows the book and a catalog search prefilled with its title and author, listing candidates and provider errors (slice 29). `PUT /api/groups/{id}/match` stores a picked candidate as the book's `Audiobook` + `MetadataMatch` (one current match, raw candidate JSON) and marks the group `UserCorrected`; `GET` returns it or 204 (slice 30). The book page shows the chosen match and a "Use this match" button on each result (slice 31). Each book stores metadata values in `AudiobookField` with an origin (`file`, `provider`, `user`): `GET`/`PUT /api/groups/{id}/metadata` read them and save user edits, and picking a match copies the candidate in. User values are never overwritten, a gap in the candidate keeps the old value, and Open Library never replaces Audible (slice 32). The book page's Metadata section shows each value with its origin and a "Match: …" line where the chosen match differs, and edits save only the changed fields (slice 33). `GET /api/groups/{id}/cover` lists the files with embedded covers and the chosen cover, `GET .../files/{fileId}/cover` serves an embedded JPEG or PNG, `PUT .../cover` with `{ source: "embedded", fileId }` stores it in `AudiobookCover`, and `GET .../cover/image` serves the chosen one (slice 34). `PUT .../cover` with `{ source: "provider" }` downloads the stored match's cover through `CoverDownloadClient` (https on `m.media-amazon.com` or `covers.openlibrary.org` only, type from the bytes, 502 on a failed download) (slice 35). `PUT .../cover/upload` stores an uploaded JPEG or PNG (multipart `file`, at most 10 MB, typed by its bytes), and `services/api.ts` has typed calls for all cover endpoints (slice 36).
- **Tests:** both test projects exist and all four checks pass.

## Phase
Phase 5, Metadata: in progress. Audible search (27), the Open Library fallback (28) and the book page's search UI (29) and picking a match (30–31) work, values with origins are stored and kept across picks (32), the editor shows current vs proposed values with their origin and saves edits (33), which also covers manual completion with no match, and the embedded (34), match's (35) or uploaded (36) cover can be chosen through the API. Left: the book page's cover picker (37). Excluded files never get an `Audiobook`.

## Last slice / next slice
- Last: 36 cover-upload-api, Implemented
- Next: 37, the cover picker on the book page (not written)

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
- 2026-10-18 (slice 10): Grouping is deterministic and rule-based within one container folder. Confidence is the number of agreeing signals (2+ High, 1 Medium, conflict Low). Untagged root files with no shared stem are unresolved.
- 2026-10-18 (slice 11): Groups are fully replaced after each completed scan (a shortcut until phase 4). SQLite foreign keys are enforced on every connection.
- 2026-10-18 (slice 13): Any user correction marks the whole group `UserCorrected`, which freezes its membership; rescans skip its files. New columns on existing tables are added by a guarded `ALTER TABLE ... ADD COLUMN` in `DatabaseInitializer`.
- 2026-10-18 (slice 17): Merge and split aren't separate endpoints. Merge moves all of B's files into A (B is deleted when empty); split creates a user-made group (`High`, "Grouped by the user") from the chosen files.
- 2026-10-18 (slice 18, developer): An excluded file stays excluded until the user undoes it, survives rescans, and can be sent back to unresolved. Unresolved files stay unresolved across rescans.
- 2026-10-18 (slice 20): The exclude and keep-unresolved states live in `AudioFile.UserState`. The grouping engine skips any file with a state; placing a file in a group clears it, and undoing an exclude sends the file to unresolved.
- 2026-10-18 (developer): Phase 4 is reopened for its leftovers. The grouping-rule tweaks are dropped (no real-collection list). Group ids must stay stable: never reused after a group is deleted. (Done in slice 24: `FileGroup` is `AUTOINCREMENT`, and old databases are rebuilt once at startup.)
- 2026-10-18 (slice 18): A bug found by a manual check inside a slice gets its own `fix: slice NN` commit, with the developer's OK.
- 2026-10-19 (slice 27): Providers sit behind `IMetadataProvider` and throw only `MetadataProviderException`, which the API returns as a 502 problem. Search candidates aren't stored until a match is picked. The series part stays a string, and format is passed through from the provider.
- 2026-10-19 (slice 28): Providers are tried in registration order (Audible, then Open Library) and the first non-empty result wins; there's no merging. Open Library candidates leave publisher and language empty because its search data mixes print editions.
- 2026-10-19 (slice 29): The book page is keyed by group id (`/books/:groupId`); the `Audiobook` will stay one-to-one with its group, so the URL can stay.
- 2026-10-19 (slice 30): Picking a match counts as a user correction (`UserCorrected = 1`) so rescans keep it. One current `MetadataMatch` per `Audiobook`; both cascade-delete with the group. The client's candidate is validated, not re-fetched.
- 2026-10-19 (slice 32): Metadata values are one `AudiobookField` row per field (JSON value, origin, provider). In a PUT, `null` means "not sent" and `""`/`[]` is an explicit empty user value. The group title and author are a read-time `file` fallback, never stored.
- 2026-10-19 (slice 33): "Proposed" in the editor means the chosen match's value, shown only where it differs from the stored one. List fields are edited one name per line so names with commas stay whole.
- 2026-10-19 (developer): Manual completion with no match is met by slice 33's editor; no separate "complete" state.
- 2026-10-19 (slice 34): The chosen cover is stored as a BLOB in `AudiobookCover` (one per book, cascade-deleted), not as a file under `/data`. Only JPEG and PNG are accepted or served, at most 10 MB. Choosing a cover marks the group `UserCorrected`.
- 2026-10-19 (slice 35): Provider covers are downloaded only from the stored match's URL, never a URL in the request, and only over `https` from the two catalog image hosts. Only the first URL is checked, because Open Library redirects to archive.org (marked as a shortcut).
- 2026-10-19 (slice 35, developer): A flaky test is fixed, not accepted. `UnresolvedFiles` resets its selection during render, not in an effect, so an early tick isn't lost.

## Verification
Full suite: backend 184 passed / 0 failed, frontend 183 passed / 0 failed. Stop the dev API before building: it locks `bin/`. Typecheck and build clean. No accepted failures. The tests run against dev ffmpeg 9.0; the image has 6.1.
