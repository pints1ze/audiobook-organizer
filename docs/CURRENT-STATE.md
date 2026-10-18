# Current state: AudiobookOrganizer

_Updated: project plan, 2026-10-18, commit none_

## Branch
`phase/1-foundation`

## Where things stand
Planning only. PRD and PLAN are written; no code exists yet.

## Phase
Current phase 1, Foundation: everything.

## Last slice / next slice
- Last: none
- Next: 01 Foundation

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

## Verification
Full suite: no tests yet.
