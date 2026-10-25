# Audiobook Library Manager (AudiobookOrganizer)

**Problem:** Audiobooks arrive as one M4B or dozens of MP3s, with misleading folder names, several books per folder, or one book split across nested folders, and with missing or wrong tags. Identifying, tagging, renaming and filing each one by hand takes several tools and a lot of repetition.
**Users:** One technically capable collector running the app on their own server or home network, and using it whenever new books land in the incoming folder.
**Goal:** Point the app at an incoming folder, review the books it proposes, confirm metadata and move consistently tagged files into an organized library without risking the originals. Identifying, finalizing and approving a book should take a glance and a couple of actions, from one screen (v1.1).

Full background: `overview.md` (v0.1 draft). This PRD supersedes it where they differ.

## Core workflows
1. **Configure:** set the incoming and destination directories (validated as existing and accessible), the directory and file naming templates, and the "keep source files" toggle. (v1)
2. **Scan:** start a manual, read-only, recursive scan of incoming. It inventories MP3 and M4B files: path, size, duration, tags, track/disc, embedded cover, chapters and modified time. Rescans recognize unchanged files and keep earlier corrections. (v1)
3. **Group:** the app proposes audiobook groups from folder structure, filename patterns, embedded album/title/artist and track/disc numbering. Each group gets a High, Medium or Low confidence and the evidence behind it. Files with too little evidence stay unresolved. The user can merge, split, move files, exclude files, reorder files and set the title or author. All corrections persist. (v1)
4. **Identify:** search Audible's catalog by title, author or ASIN, falling back to Open Library (ISBN, print-only titles). The user picks the match. Matches are never applied automatically. A book can be completed by hand with no match. (v1)
5. **Edit and normalize:** edit one canonical metadata record per book. The app tracks where each value came from: file, provider, normalized or user. Authors, narrators and genres resolve to canonical names through aliases. The app suggests aliases but never merges them on its own. Cover options are the embedded cover, a provider download or an uploaded image. (v1)
6. **Preview:** a source → destination mapping for every file, with missing template values, duplicate targets and existing-file conflicts flagged. (v1)
7. **Process:** the user approves a book and the app processes it in the background, one book at a time: copy to the destination, write tags and cover into the copy, read them back to verify, then delete the source unless "keep source files" is on. Status and history are recorded. A failed book doesn't stop the others. (v1)
8. **Dashboard:** shows the configured directories, last scan time, counts by status, failures, and filters by status and confidence. (v1)
9. **Existing library management** with Audiobookshelf sync, library-wide normalization and duplicate detection. (later)
10. **Discovery:** follow authors and series, track releases, wishlists. (later)
11. **Review workspace:** one list-plus-detail screen. The list shows every unprocessed book with a cover thumbnail, title, author and stage (Needs match, Needs review, Ready, Queued, Done, Failed), filterable by stage. The detail pane shows the selected book's match search and chosen match, cover, the metadata values that differ, destination paths and **Approve & next**. `j`/`k` move between books, `Enter` accepts, `a` approves. Scan and its status move into the workspace header; grouping corrections stay reachable from the pane. (v1.1)
12. **Background identification:** after a scan the server searches every new book in the background (one request at a time, with a delay) and stores the top candidates with a score from title, author and runtime vs the files' duration. The pane shows them side by side. Selected books can "Accept suggested match", and selected Ready books can be approved, in one action. A match is never applied without a click. (v1.1)

## Data
- **AudioFile:** source path, size, duration, mtime, extracted tags, track/disc, cover present, chapter count, sequence. It belongs to at most one FileGroup.
- **FileGroup:** proposed or confirmed set of files, confidence, evidence, unresolved/excluded flags, user-corrected flag.
- **Audiobook:** the canonical metadata record (fields in `overview.md` FR-05), status, chosen cover, ASIN/ISBN. It has one FileGroup.
- **Person / PersonAlias, Genre / GenreAlias:** canonical names and their variants. A book's stored author, narrator and genre names link to them by resolving through the aliases on each read (no link tables; developer, 2026-10-19).
- **BookSeries:** a normalized series name. Audiobook stores the series part, which can be a decimal.
- **MetadataMatch / MetadataSourceRecord:** a provider candidate, its score and the raw values retrieved.
- **ScanRun, ProcessingJob:** scan results, file operations, source/destination paths, timestamps, errors.
- **ApplicationSetting:** directories, templates, the keep-source toggle.
- Each metadata value records its origin: file, provider, normalized or user.

- **MetadataCandidate:** per book, a stored search result: provider, candidate JSON, score, searched-at time. Cascade-deleted with its group (v1.1; reverses slice 27's "candidates aren't stored").

## Rules
- A background search never applies a match. A provider failure leaves the book at Needs match with the error shown. Candidates are refreshed only for new books or on request.
- Scanning never modifies files. Nothing is moved or tagged without explicit per-book approval.
- Source files are never tagged in place. The tags are written to the destination copy.
- The source is deleted only after the copy and tag read-back succeed. Cross-volume moves are always copy, then verify, then delete.
- Existing files at the destination are never overwritten silently. A conflict blocks that book until it's resolved or skipped.
- Existing chapters and audio content must survive tagging. Unrelated tags are preserved where practical.
- Retrying a book must not create duplicate output files.
- User-entered values survive a new metadata search and rescans of unchanged files.
- When a provider has no value for a field, the existing value is kept.
- Audiobook-edition data is preferred over print-edition data. Print data never replaces known audiobook values.
- Templates support `%artist% %album% %series% %series-part% %track% %disc% %title% %year% %narrator%`, padding like `%track:3%`, and `[optional sections]` that drop out when a token inside them is empty. Output is sanitized for the platform and can never escape the destination directory.
- Only the configured directories may be read or written.
- Provider calls respect rate limits. A provider failure never blocks manual editing.

## Roles
| Role | Can |
|---|---|
| User (single, no login) | Everything. The app runs on a trusted LAN. |

## Constraints
- Runs in Docker (Linux container, Docker Compose) in Docker Desktop (WSL2) on the Audiobookshelf server (Windows). The database is on a named volume; incoming and library folders are bind-mounted from its local disks and may be on different volumes.
- Stack per AGENTS.md: .NET 10 API, SQLite + Dapper, React 19 + Vite. TagLibSharp handles tag read/write.
- **Primary provider:** Audible's catalog search, `https://api.audible.com/1.0/catalog/products?response_groups=contributors,media,product_desc,product_attrs,product_extended_attrs,series&image_sizes=...&num_results=50&products_sort_by=Relevance&keywords=...`. It is unofficial, keyless, `.com` marketplace only, and sits behind a provider adapter.
- **Fallback provider:** the Open Library API.
- Custom tags for narrator, series, ASIN and format follow Audiobookshelf's conventions so Audiobookshelf reads them correctly.
- The schema should leave room for the later phases (library management, discovery) without replacing core services.

## Out of scope
- Audiobookshelf sync, library-wide correction, duplicate detection
- Directory watching, scheduled scans
- AI identification, auto-applied matches
- Transcoding, merging MP3 → M4B, splitting, chapter creation or editing
- Acoustic fingerprinting
- Multiple users, authentication, internet exposure
- Discovery, following, lists, acquisition
- Mobile apps
- Populating genres from providers (genres are entered by hand in v1)
- Audible marketplaces other than `.com`
- Mp3tag-style template functions (`$if`, `$num`, …)

## Open questions
- [NEEDS CLARIFICATION: exact ID3 frames and MP4 atoms Audiobookshelf reads for narrator, series, series part, ASIN (resolve with fixtures in the processing phase)]
- Background search throttle (developer, 2026-10-25): one search at a time with a 2-second gap. Audible publishes no limit; revisit if it throttles or blocks.
- [NEEDS CLARIFICATION: edition handling for multiple narrators, abridged vs unabridged, regional releases. Is "user picks the match" enough for v1?]
- [NEEDS CLARIFICATION: genre source and taxonomy (deferred; manual in v1)]
