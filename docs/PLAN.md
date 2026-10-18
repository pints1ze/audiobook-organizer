# Plan: AudiobookOrganizer

**Goal:** Point the app at an incoming folder, review the books it proposes, confirm metadata and move consistently tagged files into an organized library without risking the originals.
**Sources:** docs/PRD.md; overview.md (original v0.1 requirements draft).

## Finished when
- [ ] all v1 workflows work end to end
- [ ] full automated suite passes
- [ ] Docker image runs on the home server with mounted incoming and library volumes

## Phases

Each phase gets its own `phase/N-short-name` branch and one PR to `main`.

### 1. Foundation (done)
**Goal:** project runs locally and in Docker with tests and a health check.
**Done when:** four checks pass; API health endpoint returns 200; `docker compose up` serves the client and API with a SQLite file on a mounted volume.
**Slices:** 01 (implemented), 02 (implemented), 03 (implemented).

### 2. Settings & scan (in progress)
**Goal:** configure directories and inventory the incoming folder without touching it.
**Done when:** validated directory settings save; a manual scan records every MP3/M4B with tags, duration, cover and chapter info, and a rescan skips unchanged files; dashboard shows last scan and counts; a TagLibSharp write-to-copy test proves M4B chapters and audio survive tagging.
**Slices:** 04. Carried in: a layout shell with navigation and a not-found route (from slice 03).

### 3. Grouping (not started)
**Goal:** scanned files become proposed audiobook groups with confidence and evidence.
**Done when:** PRD grouping scenarios A–E pass as automated tests; low-evidence files show as unresolved; Incoming screen lists groups with title, author, file count, duration, confidence.
**Slices:** none yet

### 4. Group corrections (not started)
**Goal:** the user can fix any grouping the rules got wrong.
**Done when:** merge, split, move file, exclude, reorder and set title/author work from the UI; corrections survive a rescan of unchanged files.
**Slices:** none yet

### 5. Metadata (not started)
**Goal:** identify a book and settle its canonical metadata.
**Done when:** Audible catalog and Open Library searches return candidates the user can pick; the editor shows current vs proposed values with their origin and keeps user edits across re-searches; cover can come from the embedded image, a download, or an upload; a book can be completed manually with no match.
**Slices:** none yet

### 6. Normalization (not started)
**Goal:** authors, narrators and genres resolve to canonical names.
**Done when:** aliases map variants to one canonical entry; the app suggests but never auto-merges aliases; a management screen lets the user create, merge and edit canonical entries.
**Slices:** none yet

### 7. Naming & preview (not started)
**Goal:** see exactly where every file will go before anything moves.
**Done when:** the template engine handles tokens, padding and optional sections with sanitized, traversal-safe output; the preview screen shows source → destination per file and flags missing values, duplicate targets and existing-file conflicts.
**Slices:** none yet

### 8. Processing (not started)
**Goal:** approved books are copied, tagged, verified and filed safely in the background.
**Done when:** an approved book is copied, tagged (Audiobookshelf-compatible custom fields, chosen cover embedded), read back and the source removed unless "keep source files" is on; failures leave sources intact and are visible with details; retry creates no duplicates; one failed book doesn't block others.
**Slices:** none yet. Carried in: the container user or PUID/PGID for files written to the mounts (from slice 03).

### 9. Hardening & release (not started)
**Goal:** trustworthy on a real collection.
**Done when:** end-to-end tests run against fixture audiobooks through scan → process; an interrupted job resumes or fails cleanly after restart; repeat scans don't duplicate processed books; Docker image documented and running on the home server.
**Slices:** none yet
