# Plan: AudiobookOrganizer

**Goal:** Point the app at an incoming folder, review the books it proposes, confirm metadata and move consistently tagged files into an organized library without risking the originals.
**Sources:** docs/PRD.md; overview.md (original v0.1 requirements draft).

## Finished when
- [x] all v1 workflows work end to end
- [x] full automated suite passes
- [x] Docker image runs in Docker Desktop on the Audiobookshelf server (Windows) with incoming and library bind-mounted from its local disks
- [ ] most books go from scanned to approved in the review workspace with a glance and two actions

## Phases

Each phase gets its own `phase/N-short-name` branch and one PR to `main`.

### 1. Foundation (done)
**Goal:** project runs locally and in Docker with tests and a health check.
**Done when:** four checks pass; API health endpoint returns 200; `docker compose up` serves the client and API with a SQLite file on a mounted volume.
**Slices:** 01 (implemented), 02 (implemented), 03 (implemented).

### 2. Settings & scan (done)
**Goal:** configure directories and inventory the incoming folder without touching it.
**Done when:** validated directory settings save; a manual scan records every MP3/M4B with tags, duration, cover and chapter info, and a rescan skips unchanged files; dashboard shows last scan and counts; a TagLibSharp write-to-copy test proves M4B chapters and audio survive tagging.
**Slices:** 04 (implemented), 05 (implemented), 06 (implemented), 07 (implemented), 08 (implemented), 09 (implemented). The tag round-trip went to ffmpeg for M4B per the slice 04 decision.

### 3. Grouping (done)
**Goal:** scanned files become proposed audiobook groups with confidence and evidence.
**Done when:** PRD grouping scenarios A–E pass as automated tests; low-evidence files show as unresolved; Incoming screen lists groups with title, author, file count, duration, confidence.
**Slices:** 10 (implemented), 11 (implemented), 12 (implemented).

### 4. Group corrections (done)
**Goal:** the user can fix any grouping the rules got wrong.
**Done when:** merge, split, move file, exclude, reorder and set title/author work from the UI; corrections survive a rescan of unchanged files.
**Slices:** 13 (implemented), 14 (implemented), 15 (implemented), 16 (implemented), 17 (implemented), 18 (implemented), 19 (implemented), 20 (implemented), 21 (implemented), 22 (implemented), 23 (implemented), 24 (implemented), 25 (implemented), 26 (implemented). Merge, split, move file, exclude (with undo), reorder and set title/author all work from the UI, and all survive rescans. Reopened 2026-10-18 at the developer's request and finished: dashboard book and file-state counts through `DashboardService` (22–23), stable group ids (24), the "Corrected by the user" evidence line (25), and Incoming polish (26). Grouping-rule tweaks were dropped by the developer.

### 5. Metadata (done)
**Goal:** identify a book and settle its canonical metadata.
**Done when:** Audible catalog and Open Library searches return candidates the user can pick; the editor shows current vs proposed values with their origin and keeps user edits across re-searches; cover can come from the embedded image, a download, or an upload; a book can be completed manually with no match.
**Slices:** 27 (implemented), 28 (implemented), 29 (implemented), 30 (implemented), 31 (implemented), 32 (implemented), 33 (implemented), 34 (implemented), 35 (implemented), 36 (implemented), 37 (implemented). Manual completion with no match is met by 33's editor (developer, 2026-10-19). Carried in: skipping files the user excluded (`AudioFile.UserState`, from slice 20).

### 6. Normalization (done)
**Goal:** authors, narrators and genres resolve to canonical names.
**Done when:** aliases map variants to one canonical entry; the app suggests but never auto-merges aliases; a management screen lets the user create, merge and edit canonical entries.
**Slices:** 38 (implemented), 39 (implemented), 40 (implemented), 41 (implemented), 42 (implemented), 43 (implemented). People and genres APIs with aliases (38–39), merge (40), suggestions (41), the People and Genres pages (42–43). Linking book values to canonical entries moved to phase 7 (developer, 2026-10-19). Dismissing suggestions is still an open question.

### 7. Naming & preview (done)
**Goal:** see exactly where every file will go before anything moves.
**Done when:** the template engine handles tokens, padding and optional sections with sanitized, traversal-safe output; a book's authors, narrators and genres link to canonical entries (exact alias matches resolve automatically, anything else is suggested) and linked values show the `normalized` origin; the preview screen shows source → destination per file and flags missing values, duplicate targets and existing-file conflicts.
**Slices:** 44 (implemented), 45 (implemented), 46 (implemented), 47 (implemented), 48 (implemented), 49 (implemented), 50 (implemented), 51 (implemented), 52 (implemented), 53 (implemented). Template engine with sanitizing and `ResolveUnder` (44), templates in settings (45), book people links API (46), the book page People section (47), the book genres field and links API (48), genres in the metadata editor (49) and the Genres links section (50); linking is done. Book-to-token mapping and the per-book preview API (51), the library-wide preview API with duplicate and existing-file flags (52) and the Preview screen (53). Done when lines met; manual checks for 45–53 go to the developer's phase verification. Linking was added 2026-10-19 with the developer's OK.

### 8. Processing (done)
**Goal:** approved books are copied, tagged, verified and filed safely in the background.
**Done when:** an approved book is copied, tagged (the Mp3tag script's fields, on MP3 and M4B, chosen cover embedded), read back and the source removed unless "keep source files" is on; failures leave sources intact and are visible with details; retry creates no duplicates; one failed book doesn't block others.
**Slices:** 54 (implemented), 55 (implemented), 56 (implemented), 57 (implemented), 58 (implemented), 59 (implemented), 60 (implemented), 61 (implemented), 62 (implemented). Approval into the queue with snapshotted destinations (54); the background worker copying queued books without overwriting, cleaning up failed jobs (55); standard tags and the chosen cover written into each copy (56); read-back of each copy and source deletion unless keep-source (57); Approve on the Preview page (58); the Jobs page with failure details and Retry, with tests proving retry creates no duplicates (59); the M4B freeform-atoms spike (60); M4B copies remuxed without `chpl` and tagged by TagLibSharp (61, the developer's choice of candidate B); the Mp3tag script's fields on both formats (62). Carried in: each file's track tag is written as its position in book order (one-file books get 1; developer, 2026-10-19); the container user or PUID/PGID for files written to the mounts (from slice 03); M4B custom fields under ffmpeg's limits (from slice 04); never processing excluded files (from slice 20).

### 9. Hardening & release (done)
**Goal:** trustworthy on a real collection.
**Done when:** end-to-end tests run against fixture audiobooks through scan → process, including the backend suite inside the Docker image (its ffmpeg 6.1 vs dev's 9.0); an interrupted job resumes or fails cleanly after restart; repeat scans don't duplicate processed books; Docker image documented and running on the Audiobookshelf server (Windows) in Docker Desktop with incoming and library bind-mounted from its local disks.
**Slices:** 63 (implemented), 64 (implemented), 65 (implemented), 66 (implemented), 67 (implemented), 68 (implemented), 69 (implemented), 70 (implemented), 71 (implemented), 72 (implemented), 73 (implemented), 74 (implemented), 75 (implemented). M4B copies carry no chapters (63; developer, 2026-10-21, option A), after a real book's copy kept 6 of its 27 chapters; MP3 chapters are kept. The backend suite runs inside a Linux image with the runtime's ffmpeg 6.1 through `docker build --target test .` and passes (64). A job cut off by a restart fails cleanly: startup removes its copies and `.partial` unless a source was already deleted, so the book can be approved again (65). End-to-end tests run fixture books through scan → process → rescan with keep-source off and on: no new groups, no second approval, no second copies (66). Processed books (a `Succeeded` job) leave the Incoming and Preview lists and the dashboard counts, and approving one again is refused (67). Once a processed book's sources are deleted, their `AudioFile` rows and the incoming folders left empty are removed (68). At startup a job cut off while deleting sources finishes the deletions and is marked `Succeeded` (69). `docs/DEPLOY.md` and `.env.example` document running the image on the home server (70); the home-server run itself is the developer's. The host is Docker Desktop on the developer's Windows machine: `docs/deploy/compose.yaml` runs the repo-built image with a named data volume and CIFS volumes on `//cookserv/d` (71). Serilog also writes daily log files under `/data/logs`, kept 14 days (72). The host is now the Audiobookshelf server (Windows, Docker Desktop), with `/incoming` and `/library` bind-mounted from its local `D:` folders; CIFS is gone (73). The Jobs status line re-focuses on a repeated announcement and the Incoming load error takes focus when a reload fails (74). The cover picker marks the match's cover "Current cover" only when it came from the current match (75). Carried in: the intermittent `JobsPage` Retry test failure, accepted for now (from slice 62); an unidentified backend test that failed once in 10 runs, accepted for now (developer, 2026-10-24, slice 74); the Mp3tag Extended Tags comparison of copies against a script-tagged file (from slice 62); re-focusing the Jobs status line on a repeated announcement (from slice 59); focusing the Incoming load error when a failed save also fails the reload (from slice 26); the cover picker marking a provider cover "Current cover" after the match changes (from slice 37).

### 10. Review workspace (in progress)
**Goal:** finalize and approve books from one list-plus-detail screen.
**Done when:** the home page lists every unprocessed book with cover, title, author and stage, filterable by stage; selecting a book shows match search, cover, changed metadata, destination and Approve & next in a detail pane without leaving the page; `j`/`k`/`Enter`/`a` work and the screen meets WCAG 2.1 AA.
**Slices:** 76 (implemented), 77 (implemented), 78 (implemented), 79 (implemented), 80 (implemented), 81 (implemented), 82 (implemented), 83 (implemented). shadcn packages, tokens, the dropdown menu and the new header with a More menu (76). `GET /api/review` lists unprocessed books with a stage and counts (77). `/` is the Review list with the stage filter, covers and a placeholder pane; the Dashboard is at `/dashboard` (78). The pane shows the chosen match and the catalog search; picking one saves it and updates the stage (79). Cover and metadata in the pane, flat pane headings, and a versioned chosen-cover URL in the list (80). Destination in the pane: folder, file count and blocking problems (81). Approve & next for Ready books, moving to and focusing the next book; secondary buttons are outline (82). Shortcuts `j`/`k`/`a`/`/` and the `?` popover (83). Next: highlighted match with `Enter` and a filled Accept match (84), scan in the header (85). Desktop browsers only; the narrow layout isn't a target. UI rules: `docs/UIUX.md`.

### 11. Parallel identification (not started)
**Goal:** books arrive with suggested matches, so identifying takes a glance.
**Done when:** after a scan every new book is searched in the background (throttled) and its candidates stored with a score including runtime vs duration; the detail pane shows them side by side; selected books can accept their suggested match, and selected Ready books can be approved, in one action.
**Slices:** none yet.
