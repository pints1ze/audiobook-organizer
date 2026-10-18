# Audiobook Library Manager
## Project Overview & Minimum Viable Product Requirements

**Status:** Initial Draft  
**Version:** 0.1  
**Application type:** Self-hosted web application  
**Primary user:** Individual audiobook collector  
**Initial deployment:** Docker-based, single-user installation

---

# Part 1: Project Overview

## 1. Product Vision

Audiobook Library Manager is a self-hosted application for importing, identifying, enriching, normalizing, and organizing audiobook files.

The application transforms an unorganized collection of audiobook files into a consistently tagged and structured library, regardless of how the original files were named, grouped, or stored.

It should minimize manual metadata editing, eliminate repetitive file-management tasks, and maintain consistency across a growing collection.

The long-term goal is to provide a centralized management platform for an audiobook collection, including library maintenance, Audiobookshelf integration, and audiobook discovery.

## 2. Problem Statement

Audiobooks obtained from different sources rarely follow a consistent organizational standard.

Common problems include:

- A book may be stored as one M4B file or dozens of MP3 files.
- Folder names may be descriptive, abbreviated, or misleading.
- Multiple books may exist within a single directory.
- Files belonging to one audiobook may span multiple nested directories.
- Embedded metadata may be missing, inconsistent, or incorrect.
- Authors, narrators, and genres may use inconsistent naming conventions.
- Covers, descriptions, identifiers, and series information may be missing.
- Manually renaming and tagging every file is repetitive and time-consuming.

Existing workflows often require multiple independent tools to identify a book, retrieve its information, update the metadata, and move it into the appropriate directory.

This application should consolidate those tasks into a single workflow.

## 3. Product Objectives

The application should:

1. **Identify:** Analyze incoming audio files and determine which files belong to the same audiobook.
2. **Enrich:** Retrieve accurate metadata from external book and audiobook catalogs.
3. **Normalize:** Apply consistent names and classifications to authors, narrators, and genres.
4. **Tag:** Write standardized metadata and cover artwork into audio files.
5. **Organize:** Move and rename files using customizable naming templates.
6. **Preserve:** Protect existing audio content, chapter information, and original files from accidental loss.
7. **Extend:** Establish a foundation for later library synchronization and book discovery functionality.

## 4. Intended Users

The MVP targets a single technically capable user managing a personal collection of audiobooks.

The application is designed primarily for a local or self-hosted environment, with audiobook directories accessible through mounted volumes.

Multi-user functionality, permissions, and hosted subscription services are outside the initial scope.

## 5. Product Phases

### Phase 1 — Audiobook Intake & Organization (MVP)

- Configurable incoming and destination directories
- Recursive directory scanning
- MP3 and M4B metadata extraction
- Intelligent file grouping
- Manual grouping corrections
- Book metadata search and enrichment
- Metadata editing and normalization
- Embedded metadata and cover writing
- Customizable file and directory naming
- Preview and approval before file operations
- Persistent database and processing history

### Phase 2 — Existing Library Management

- Audiobookshelf API integration
- Import existing library records
- Identify inconsistent metadata and folder structures
- Apply normalization to existing books
- Detect potential duplicates
- Reorganize existing library files
- Synchronize changes with Audiobookshelf

### Phase 3 — Audiobook Discovery

- Follow authors, narrators, and series
- Track series completeness and new releases
- Explore genres and recommendations
- Follow external book lists and collections
- Identify books missing from the local collection
- Maintain reading and acquisition wishlists

These later phases are explicitly excluded from the MVP, but the initial database and application architecture should support their eventual development.

---

# Part 2: MVP Product Requirements

## 6. MVP Definition

The MVP is successful when a user can configure an incoming directory, scan its contents, review intelligently suggested audiobook groups, retrieve and correct metadata, and safely move consistently tagged files into an organized library.

The application should not require files to follow a prescribed naming convention before scanning.

**Primary MVP workflow:**

Configure → Scan → Group → Identify → Enrich → Normalize → Preview → Process

Processing should be performed per audiobook, allowing users to approve individual books without requiring an entire scan to be resolved.

## 7. Functional Requirements

### FR-01: Directory Configuration

**Priority: Must Have**

The application shall provide a settings interface for configuring:

- Incoming directory
- Destination audiobook library directory
- Default directory naming template
- Default file naming template
- Metadata provider configuration

Requirements:

- Directories may be provided through Docker-mounted paths.
- The application must validate that configured paths exist and are accessible.
- Incoming and destination directories may reside on different volumes.
- Recursive directory scanning must be supported.
- The user can initiate a scan through the web interface.
- The application must not move or modify incoming files merely because a scan has completed.

**MVP default:** Scanning is manually initiated. Automatic monitoring and scheduled scans are future enhancements.

### FR-02: Audio File Discovery

**Priority: Must Have**

The scanner shall recursively inspect the incoming directory and identify supported audiobook files.

**MVP-supported audio formats:**
- MP3
- M4B

For each discovered file, collect:

| Property | Description |
|---|---|
| Source path | Current absolute file location |
| Filename | Original filename |
| Extension | MP3 or M4B |
| File size | Size in bytes |
| Duration | Audio playback length |
| Embedded tags | Available metadata fields |
| Track/disc number | Embedded numbering, if present |
| Embedded artwork | Cover image, if present |
| Chapter information | Existing chapters, if supported |
| Modification information | Used to identify changed files |

Non-audio files should be ignored during initial grouping, except for recognized cover artwork files that may be useful for metadata enrichment.

Repeat scans should recognize previously discovered files and preserve saved user corrections when files have not changed.

### FR-03: Intelligent Audiobook Grouping

**Priority: Must Have**

The application shall propose logical audiobook groups using multiple sources of evidence.

Grouping should consider:

1. Directory structure
2. Parent folder naming
3. Filename patterns
4. Embedded album metadata
5. Embedded title metadata
6. Embedded artist/author metadata
7. Track and disc numbering
8. Similarity between related files

No single property should be considered universally authoritative.

#### Required grouping scenarios

**Scenario A: Single-file audiobook**

    /incoming/ready-player-one.m4b

Expected result: One audiobook containing one file.

**Scenario B: Multi-file audiobook**

    /incoming/ready player one - ernest cline/
        track1.mp3
        track2.mp3
        track3.mp3

Expected result: One audiobook containing three files.

**Scenario C: Multi-disc audiobook**

    /incoming/rpo - ernest cline/
        disc 1/track1.mp3
        disc 1/track2.mp3
        disc 2/track1.mp3
        disc 2/track2.mp3

Expected result: One audiobook containing four files, ordered by disc and track.

**Scenario D: Multiple books in one folder**

    /incoming/
        01-rpo1-ernestcline.mp3
        02-rpo1-ernestcline.mp3
        03-rpo1-ernestcline.mp3
        01-rpo2-ernestcline.mp3
        02-rpo2-ernestcline.mp3
        03-rpo2-ernestcline.mp3

Expected result: Two proposed audiobook groups containing three files each.

**Scenario E: Consistent embedded metadata**

Multiple files with matching album and author information should be considered strong candidates for grouping, even if filenames are generic.

#### Confidence and ambiguity

Grouping results should include a confidence level:

- High: Strong agreement between multiple signals
- Medium: Reasonable match with limited evidence
- Low: Ambiguous grouping requiring user review

The application should explain the primary evidence used to propose each group.

Files with insufficient evidence should appear as unresolved rather than being silently assigned to an unrelated audiobook.

#### Manual correction

The user must be able to:

- Merge two or more proposed groups.
- Split one group into multiple groups.
- Move individual files between groups.
- Mark files as unrelated or excluded.
- Modify file ordering.
- Provide or correct an audiobook title or author.

Manual grouping changes must persist in the database.

**MVP constraint:** Deterministic rules and similarity scoring are preferred over requiring an AI model. AI-assisted classification may be added later.

### FR-04: Book Identification & Metadata Lookup

**Priority: Must Have**

Once files have been grouped, the application shall search external metadata providers to identify the audiobook.

The user should be able to search using:

- Book title
- Author
- ISBN
- Audible ASIN
- Other available identifiers

The application should propose matching catalog records and allow the user to select the correct result.

Potential providers include:

- Open Library
- Google Books
- Hardcover
- Audible or a compatible audiobook-specific metadata source

Metadata sources should be implemented through interchangeable provider adapters.

#### Matching considerations

A matching score should account for:

- Exact identifiers
- Title similarity
- Author similarity
- Series information
- Publication details
- Audiobook edition information, where available

Matching the correct audiobook edition is important.

A print edition and an audiobook edition may have different publishers, publication dates, ISBNs, descriptions, or contributors.

The application should avoid replacing known audiobook-specific metadata with unrelated print-edition information.

**MVP recommendation:** Implement Open Library and one audiobook-focused provider first. Additional providers can follow without changing the main processing workflow.

### FR-05: Metadata Management

**Priority: Must Have**

The application shall maintain one canonical metadata record for each identified audiobook.

| Metadata field | Expected behavior |
|---|---|
| Title | Canonical book title |
| Album | Audiobook title |
| Album Sort | Normalized sorting title |
| Author / Artist | Canonical author name(s) |
| Album Artist | Primary author or configured value |
| Narrator | One or more narrators |
| Composer | Independently managed contributor field |
| Series | Series name, if applicable |
| Series Part | Position in series, including decimals |
| Genre | Normalized genre classification |
| Description | Book description |
| Comment | Optional separate commentary |
| Publisher | Audiobook publisher, when available |
| Publication Year | Year for the matched edition |
| Language | Normalized language value |
| ISBN | Edition-specific ISBN |
| ASIN | Audible identifier |
| Audible ASIN | Audible-specific identifier |
| Format | Abridged, unabridged, or unknown |
| Track | Track number |
| Disc | Disc number, where applicable |
| Cover Image | Embedded cover artwork |

Multiple authors and narrators must be supported.

When an external source does not provide a value, existing metadata should be retained unless the user explicitly changes it.

Metadata should distinguish between:

- Values extracted from existing files
- Values retrieved from providers
- Values normalized by the application
- Values manually entered or overridden by the user

#### File-specific versus book-level metadata

Most fields are shared by all files belonging to an audiobook.

Track number, disc number, and file title are file-specific.

For example:

**Book title / album:** Heartwood  
**Track title:** Heartwood - 001  
**Track number:** 1

This separation allows consistent book-level metadata while preserving individual file identity.

### FR-06: Metadata Normalization

**Priority: Must Have**

The application shall maintain a database of normalized names and classifications.

The initial normalization targets are:

- Authors
- Narrators
- Genres

Examples:

    Stephen King
    Stephen  King
    KING, STEPHEN

May all resolve to:

    Stephen King

Another example:

    Sci-Fi
    Science Fiction
    Science-Fiction

May resolve to:

    Science Fiction

The database should support:

- Canonical entity names
- Alternative names or aliases
- Source-specific naming variants
- Manual alias creation
- Reuse of existing canonical entries

Normalization should occur before final metadata writing and file organization.

**Important:** The system should suggest likely aliases but must not automatically merge different people or unrelated genre concepts based solely on name similarity.

### FR-07: Metadata Editing Interface

**Priority: Must Have**

The user shall be able to review and edit an audiobook's metadata before processing.

The editing interface should display:

- Proposed title, author, and narrator
- Matching catalog records
- Current metadata
- Proposed metadata
- Existing and candidate cover artwork
- Series information
- Genre normalization
- Files included in the audiobook
- File sequence and track numbers

Users should be able to manually override any editable metadata value.

User changes must be retained if a metadata search is performed again.

A book must be organizable manually even when no external catalog match is found.

### FR-08: Embedded Metadata Writing

**Priority: Must Have**

The application shall write approved metadata directly into supported audio files.

For MP3, use appropriate ID3 metadata frames.

For M4B, use appropriate MP4 metadata atoms and compatible custom fields where required.

Requirements:

- Preserve the original audio content.
- Preserve existing chapter information.
- Preserve unrelated metadata where practical.
- Embed selected cover artwork.
- Support track-specific metadata.
- Avoid unnecessary modification of unsupported metadata fields.
- Verify written metadata by reading it back.
- Surface unsupported fields or failed writes to the user.

The application must recognize that some fields, including narrator, series, ASIN, and format, may require format-specific custom metadata storage.

Custom fields should be mapped consistently without assuming that all third-party players recognize them.

**MVP constraint:** Audio conversion, merging MP3 files into M4B, and chapter generation are not included.

### FR-09: File Naming & Directory Organization

**Priority: Must Have**

The application shall support customizable destination paths and filenames generated from approved metadata.

Templates should support common metadata tokens:

    %artist%
    %album%
    %series%
    %series-part%
    %track%
    %disc%
    %title%
    %year%
    %narrator%

Required formatting capabilities:

- Token substitution
- Optional sections
- Numeric padding
- Conditional inclusion of series information
- Nested directories
- Platform-safe filename generation

Example desired output:

    D:\audiobooks\
        Ernest Cline\
            Ready Player One\
                001. Ready Player One (Ernest Cline).mp3
                002. Ready Player One (Ernest Cline).mp3

Series-aware example:

    D:\audiobooks\
        Brandon Sanderson\
            Mistborn\
                Book 01 - The Final Empire\
                    001. The Final Empire.mp3

Single-file example:

    D:\audiobooks\
        Ernest Cline\
            Ready Player One\
                Ready Player One (Ernest Cline).m4b

The application should provide a default naming template and allow it to be modified.

#### Preview requirement

Before modifying any files, the interface must show:

- Original path
- Proposed destination path
- Proposed filename
- Potential conflicts
- Missing template values

The user must approve the operation before execution.

#### Collision handling

When the destination already exists, the application must not silently overwrite it.

Conflicts should require explicit resolution or cause that audiobook to be skipped.

### FR-10: Processing Workflow

**Priority: Must Have**

Each audiobook should have a persistent processing status.

Suggested statuses:

1. Discovered
2. Grouped
3. Needs Review
4. Metadata Ready
5. Ready to Process
6. Processing
7. Completed
8. Failed

A completed audiobook should retain:

- Original source paths
- Final destination paths
- Associated metadata record
- Processing timestamp
- Operation results
- Error details, if applicable

The system should process audiobooks independently so that one failed book does not prevent other approved books from completing.

#### Safety requirements

- Scanning must be read-only.
- Processing requires explicit approval.
- Before destructive operations, the system must verify that destination writes succeeded.
- Cross-volume transfers must use a copy-and-verify approach before removing source files.
- Source files must remain intact when a transfer fails.
- Existing chapters must not be discarded.
- Processing failures must be clearly visible.
- Retry operations must avoid creating duplicate output files.

A preview-only operation must be available without modifying files.

The MVP does not require a full undo system, but must maintain sufficient history to investigate failed operations.

### FR-11: Dashboard and Processing Queue

**Priority: Must Have**

The application shall provide a simple web-based dashboard.

The dashboard should show:

- Configured incoming and destination directories
- Last scan time
- Number of discovered audiobook groups
- Books needing review
- Books ready to process
- Successfully processed books
- Failed operations

Users should be able to navigate from the dashboard to an audiobook's detail view.

Filters should include processing status and confidence level.

The primary actions are:

- Scan Incoming Directory
- Review Group
- Search Metadata
- Edit Metadata
- Preview Organization
- Process Audiobook

---

# Part 3: Suggested Architecture

## 8. Technology Stack

The following implementation choices are recommended defaults rather than mandatory product requirements.

| Component | Recommended Technology |
|---|---|
| Frontend | React + Vite |
| UI Styling | Tailwind CSS + DaisyUI |
| Backend | ASP.NET Core API |
| Database | PostgreSQL |
| Data Access | Dapper or EF Core |
| Metadata Extraction | TagLib# or equivalent |
| Audio Inspection | FFprobe, where needed |
| Advanced Tag Handling | Format-specific metadata adapters |
| Background Processing | .NET hosted background service |
| Deployment | Docker Compose |
| Logging | Serilog |

The application's main business logic should remain in .NET.

A separate metadata-processing service may be introduced if a different library or language provides substantially better audio tag support.

## 9. Architecture Components

**Scanning Service**

Enumerates incoming files, extracts existing metadata, and stores a discovered-file inventory.

**Grouping Engine**

Analyzes files and produces suggested audiobook groupings with supporting evidence and confidence scores.

**Metadata Service**

Searches external metadata providers, maps responses to a common internal model, and proposes enrichment.

**Normalization Service**

Resolves alternative author, narrator, and genre names to canonical database records.

**Tagging Service**

Writes validated metadata into MP3 and M4B files using format-specific adapters.

**Organization Service**

Generates destination paths, checks conflicts, and executes verified file operations.

**Processing Service**

Coordinates background tasks and maintains processing status and history.

**Web Interface**

Allows users to configure, review, edit, preview, and approve operations.

## 10. Core Database Entities

The database should distinguish a book's identity from its physical files and externally sourced metadata.

| Entity | Purpose |
|---|---|
| Audiobook | Canonical audiobook metadata and processing state |
| AudioFile | Physical file, extracted metadata, sequence |
| FileGroup | Proposed or confirmed collection of audio files |
| BookSeries | Series identity and normalized title |
| Person | Canonical author or narrator identity |
| PersonAlias | Alternate person names |
| Genre | Canonical genre |
| GenreAlias | Alternate genre values |
| AudiobookContributor | Relationship between audiobook, person, and role |
| AudiobookGenre | Relationship between audiobook and genres |
| MetadataMatch | External catalog candidate and confidence |
| MetadataSourceRecord | External provider ID and retrieved values |
| ScanRun | Scan activity and results |
| ProcessingJob | File operations, state, and errors |
| ApplicationSetting | Directory paths and naming templates |

For future expansion, the data model should also distinguish a conceptual book/work from its audiobook recording or edition when external catalog information permits.

The MVP does not require a complete bibliographic catalog model.

---

# Part 4: User Experience

## 11. Primary Screens

### Screen 1: Dashboard

Purpose: Present the state of the incoming directory and processing queue.

Primary actions:

- Scan
- Review pending books
- View completed processing jobs
- Open settings

### Screen 2: Incoming Audiobooks

Purpose: Review suggested groups identified during scanning.

Display proposed audiobook groups with:

- Estimated title and author
- File count
- Total duration
- Grouping confidence
- Current status

Users can inspect or correct groups before continuing.

### Screen 3: Audiobook Detail & Metadata Editor

Purpose: Identify and prepare a book for processing.

Suggested layout:

- Left: Files belonging to the audiobook
- Center: Editable canonical metadata
- Right: Catalog matches and cover choices

Provide actions for searching, matching, normalizing, and approving metadata.

### Screen 4: Organization Preview

Purpose: Show exactly what will happen before a file operation.

Display a source-to-destination mapping for every file.

Identify missing metadata, duplicate target paths, and filename conflicts.

### Screen 5: Settings

Purpose: Configure directories, provider credentials, naming templates, and normalization rules.

---

# Part 5: Nonfunctional Requirements

## 12. Reliability

- Scans should tolerate unreadable files without terminating the entire scan.
- Metadata provider failures must not prevent manual editing.
- Background operations should continue independently of browser navigation.
- The application should resume safely after an unexpected restart.
- File operations should be logged.
- Database records should preserve manual corrections across rescans.

## 13. Performance

For the MVP:

- Scanning should not decode entire audio files merely to inspect metadata.
- Metadata extraction should avoid unnecessary repeated reads.
- File processing should use background operations.
- The interface should remain responsive during scanning and file transfers.
- External provider calls should respect rate limits.

## 14. Security

- Only configured and authorized directories should be scanned or modified.
- Prevent path traversal through user-supplied naming tokens.
- Sanitize generated filenames.
- Store provider credentials securely.
- Exclude credentials from logs.
- The initial deployment may operate as a single-user application on a trusted local network.

Authentication and remote internet exposure are not MVP objectives.

---

# Part 6: Acceptance Criteria

## 15. MVP Completion Checklist

The MVP will be considered functionally complete when:

- [ ] A user can configure incoming and destination directories.
- [ ] A recursive scan discovers MP3 and M4B files.
- [ ] A single-file audiobook is correctly grouped.
- [ ] Multi-file audiobooks are grouped using folder and metadata evidence.
- [ ] Multi-disc audiobooks maintain correct sequence.
- [ ] Multiple audiobooks in the same directory can be separated.
- [ ] Uncertain groups can be corrected manually.
- [ ] The application retrieves candidate metadata from external providers.
- [ ] Users can select the appropriate book match.
- [ ] Missing metadata can be entered manually.
- [ ] Author, narrator, and genre names can be normalized.
- [ ] Approved metadata is written into MP3 and M4B files.
- [ ] Cover artwork can be embedded.
- [ ] Existing chapter information survives metadata updates.
- [ ] Naming templates generate the intended directory and file structure.
- [ ] Users can preview all file changes before execution.
- [ ] Destination conflicts cannot silently overwrite existing files.
- [ ] Files are successfully copied or moved into the destination library.
- [ ] Processing errors are visible and recorded.
- [ ] Repeat scans do not duplicate previously processed audiobooks.

---

# Part 7: Explicitly Deferred Features

## 16. Out of Scope for MVP

The following capabilities are intentionally excluded:

- Audiobookshelf synchronization
- Existing library-wide metadata correction
- Automatic directory monitoring
- Scheduled background scans
- AI-powered book identification
- Automatic file splitting or combining
- Audio transcoding
- Chapter creation or editing
- Duplicate audio detection using acoustic fingerprints
- Multiple users and permission management
- Audiobook discovery and recommendations
- Following authors, series, narrators, or genres
- Goodreads and Hardcover list subscriptions
- Automatic audiobook acquisition or downloads
- Mobile-native applications

The architecture should allow these features to be added without replacing the core metadata and organization services.

---

# Part 8: Recommended Development Order

## 17. Implementation Milestones

**Milestone 1 — Foundation**

Docker deployment, database, application settings, directory scanning, audio inventory, and basic dashboard.

**Milestone 2 — Grouping**

File grouping rules, confidence scoring, manual merge/split controls, and persistent corrections.

**Milestone 3 — Metadata**

Provider integrations, catalog matching, metadata editing, cover selection, and name normalization.

**Milestone 4 — Tagging & Organization**

Audio metadata writing, naming templates, directory preview, collision detection, and safe file processing.

**Milestone 5 — Verification**

Integration tests, chapter preservation tests, repeated scan tests, interrupted transfer recovery, and end-to-end validation.

---

# Part 9: Implementation Decisions to Finalize

## 18. Open Questions

The following choices should be finalized during technical planning rather than assumed to be settled product requirements.

1. **Metadata providers:** Which audiobook-specific service offers sufficient API stability, coverage, and acceptable usage terms?
2. **Tag compatibility:** Which custom MP3 and M4B tag mappings provide the best interoperability with Audiobookshelf and desktop tagging tools?
3. **File transfer behavior:** Should successful processing remove source files immediately or retain them for an optional verification period?
4. **Automatic matching:** Should high-confidence catalog matches ever be applied without explicit user confirmation?
5. **Naming templates:** Should the initial engine implement the complete Mp3tag-style expression syntax or a deliberately smaller subset?
6. **Cover artwork:** Should existing embedded covers be preferred over downloaded alternatives?
7. **Genre taxonomy:** Should the database preserve hierarchical source genres or normalize them into a simpler canonical list?
8. **Edition handling:** How should the application handle books with multiple narrators, abridgments, regional releases, and different audiobook editions?

These decisions should not block initial development of directory scanning, grouping, and the canonical metadata model.