# Current state: AudiobookOrganizer

_Updated: slice 02 review, 2026-10-18, commit 08d522b_

## Branch
`phase/1-foundation`

## Where things stand
The API runs on port 5070 (`scripts/start-api.ps1`), creates its SQLite file and serves `GET /api/health`. The React client runs on port 5173 (`scripts/start-client.ps1`), proxies `/api` and shows the API and database status on its dashboard. Both test projects exist. There's no Docker setup yet.

## Phase
Current phase 1, Foundation. Still to do: Docker Compose with mounted volumes, plus the database path fix and the health 503 test (03).

## Last slice / next slice
- Last: 02 client-scaffold, Implemented
- Next: 03 docker-compose

## Open questions
- Exact ID3 frames / MP4 atoms Audiobookshelf reads for narrator, series, series part, ASIN (phase 8)
- Audible catalog API rate limits and terms (phase 5)
- Edition handling: multiple narrators, abridged vs unabridged, regional releases (phase 5)
- Genre source and taxonomy (deferred; manual in v1)
## Decisions
- 2026-10-18: Docker (Linux), single user, no auth. Trusted LAN; overview.md's deployment wins over AGENTS.md's IIS/Entra template.
- 2026-10-18: SQLite + Dapper, not PostgreSQL. One user, one file on a mounted volume.
- 2026-10-18: TagLibSharp is the only tag/audio dependency. ffprobe only if it proves necessary.
- 2026-10-18: Audible catalog search is the primary provider, with Open Library as fallback. Matches are never auto-applied.
- 2026-10-18: Sources are copied, the copy is tagged and verified, then the source is deleted. The "keep source files" setting defaults to off.
- 2026-10-18: Template syntax is `%token%`, `%token:N%` padding and `[optional]` sections only.
- 2026-10-18: Covers can come from the embedded image, a provider download or an upload. The chosen file is embedded in every output file.
- 2026-10-18: Custom tags follow Audiobookshelf conventions.
- 2026-10-18: Genres are entered by hand in v1. Provider genres come later.
- 2026-10-18 (slice 01): `.gitignore` uses `*.db*` patterns instead of `data/`, because case-insensitive git would also ignore the `Data/` source folders.
- 2026-10-18 (slice 01): There's no HTTPS, auth middleware or OpenAPI in the API.
- 2026-10-18 (slice 02): The typecheck command is `npx tsc -b --noEmit`, because Vite's root tsconfig only holds references. AGENTS.md was updated with the developer's OK.

## Verification
Full suite: backend 3 passed / 0 failed, frontend 6 passed / 0 failed. The typecheck (`tsc -b`) passes. No accepted failures.
