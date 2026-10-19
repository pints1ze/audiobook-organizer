# Slice 10: Grouping engine (pure logic)

**Phase:** 3, Grouping
**Goal:** a deterministic `GroupingEngine` turns a list of scanned files into proposed audiobook groups. Each group has ordered files, an estimated title and author, a confidence level and human-readable evidence. Files without enough evidence are returned as unresolved. There's no database or UI in this slice.

## Done when
- [x] PRD/overview grouping scenarios A–E each pass as automated tests, using the exact paths from `overview.md` FR-03
- [x] Each group has a confidence (`High`, `Medium` or `Low`), at least one evidence string, an estimated title and author (possibly null), and files ordered by disc, then track, then natural filename order
- [x] Loose files at the incoming root with no album tag and no shared filename stem come back as unresolved, not as single-file groups. Exception: a single M4B is a book on its own (scenario A)
- [x] The engine is pure: the same input gives the same output, with no file system or database access
- [x] All four checks pass

## Read first
- `AGENTS.md`: Layout (logic in `Services/`), section 4 (keep it simple: deterministic rules, no AI, no new packages)
- `overview.md` FR-03, the grouping scenarios A–E. "Confidence and ambiguity" covers High = strong agreement between several signals, Medium = a reasonable match with limited evidence, Low = ambiguous and needs review, plus "explain the primary evidence" and "insufficient evidence goes to unresolved".
- `docs/PRD.md`: workflow 3 (Group)
- `src/AudiobookOrganizer.Api/Models/Local/AudioFile.cs` (the fields available as input)

## Tasks
- [x] **Input and output models.** In `Services/Grouping/`, add:
  - `GroupingInput`: `FileId`, `RelativePath` (relative to incoming, with `/` separators; the caller normalises), `Extension`, `Title`, `Album`, `Artist`, `AlbumArtist`, `Track`, `Disc` and `DurationMs`.
  - `ProposedGroup`: `EstimatedTitle`, `EstimatedAuthor`, `Confidence` (enum), `Evidence` (list of strings), `Files` (an ordered list of `FileId`s with their resolved disc and track), and `TotalDurationMs`.
  - `GroupingResult`: `Groups` and `Unresolved` (`FileId`s).

  Verify: compiles.
- [x] **Folder and filename rules.** In `GroupingEngine` (or small private helpers in the same folder):
  1. **Container folder:** a file's parent folder, except when the parent's name looks like a disc folder (case-insensitive `disc 1`, `disk2`, `cd 01`, `cd1`, `part 2`). Then the container is the grandparent, and the folder supplies the disc number.
  2. **Filename stem:** drop the extension and the leading and trailing track-number tokens and their separators (`01-`, `01 `, `track1`, `- 003`, `(01)`). Lower-case it and collapse whitespace and separators. Digits inside a word stay, so `01-rpo1-ernestcline` gives the stem `rpo1-ernestcline` and track 1.
  3. **Track from filename:** the leading or trailing number when the tag has none.
  4. **Natural sort:** `track2` comes before `track10`.

  Verify: unit tests on these helpers through the engine's public behaviour.
- [x] **Grouping and confidence.** Within each container folder:
  - Each M4B is its own group.
  - Group MP3s by the normalised album tag plus the album artist (or artist) when any exist.
  - MP3s with no album tag are grouped by filename stem.
  - In a non-root container, MP3s with no album tag and one shared stem, or no usable stem at all (like `track1`, `track2`), form one group: the folder is the evidence.
  - At the incoming root, MP3s with no album tag form groups only if 2 or more share a stem. The rest are unresolved.

  Confidence:
  - **High** when two or more independent signals agree, for example the folder plus a single album tag across all files, or a stem plus continuous track numbers 1..n.
  - **Medium** with one signal, such as the folder only or the stem only.
  - **Low** when signals conflict, such as one folder holding files whose album tags differ only slightly, or track numbers that repeat with no disc info. The group is still proposed but flagged.

  Evidence strings are short and specific, e.g. `Same folder "rpo - ernest cline"`, `Album tag "Ready Player One" on all 4 files`, `Disc folders "disc 1", "disc 2"`, `Track numbers 1–3 continuous`, `Filename pattern "rpo1-ernestcline"`.

  Estimated title: the album tag, else the folder name's "title" part when it looks like `title - author`, else the stem. Estimated author: the album artist or artist tag, else the folder name's author part. Verify: the tests below.
- [x] **Tests.** Write the tests below in `Services/GroupingEngineTests.cs`, building `GroupingInput` lists in code. No audio files are needed. Verify: `dotnet test` is green.
- Data: none (pure logic).
- Auth: none.
- Accessibility: none (no UI).

## Tests to add
- **Scenario A:** `ready-player-one.m4b` at the root gives 1 group of 1 file. Confidence is at least Medium, with evidence naming the single M4B.
- **Scenario B:** `ready player one - ernest cline/track1..3.mp3` with no tags gives 1 group of 3 in track order. The estimated title is "ready player one" and the author is "ernest cline". Medium confidence, since the folder is the only evidence.
- **Scenario C:** `rpo - ernest cline/disc 1/track1.mp3, track2.mp3, disc 2/track1.mp3, track2.mp3` gives 1 group of 4, ordered disc 1 track 1, disc 1 track 2, disc 2 track 1, disc 2 track 2, with evidence mentioning the disc folders.
- **Scenario D:** six root files `01..03-rpo1-ernestcline.mp3` and `01..03-rpo2-ernestcline.mp3` give 2 groups of 3, each in track order, with nothing unresolved.
- **Scenario E:** three generically named files (`a.mp3`, `b.mp3`, `c.mp3`) in one folder with album "Heartwood", artist "Some Author" and tracks 1–3 give 1 group with **High** confidence and evidence naming the album tag.
- **Unresolved:** a root `random.mp3` and `other thing.mp3` with no tags give 0 groups and 2 unresolved.
- **Low confidence:** a folder whose files carry tracks 1, 1, 2, 2 with no disc info or tags gives a group with **Low** confidence.
- **Determinism:** shuffling the input order gives an identical result.

## Manual checks
- none. This slice is pure logic and fully covered by unit tests. The real-collection check comes once groups are persisted and shown (slices 11–12).

## Out of scope
- Persisting groups, running grouping after a scan, and the API: slice 11.
- The Incoming screen: slice 12.
- Manual corrections (merge, split, move, exclude, reorder): phase 4.
- Grouping across different container folders (e.g. the same album tag in two unrelated folders), fuzzy album-tag similarity, and AI. Revisit if real collections need it.
- Cover image files and non-audio evidence.

## Notes
- **Conflict check:** consistent with the PRD and FR-03 (deterministic rules, every signal considered, unresolved instead of silent assignment, evidence explained). It matches AGENTS.md (no new packages, logic in `Services/`). Input is a plain record rather than `AudioFile`, so the engine stays pure and testable; slice 11 maps between the two.
- **Risk:** real collections are messier than A–E. The rules are deliberately simple and the confidence level flags doubt. Phase 4 corrections are the safety net. Expect follow-up rule tweaks after the first real scan in slice 12.
- **Size:** 4 tasks, about 5 files: 3 small model files (or one file with the records), `GroupingEngine.cs` and `GroupingEngineTests.cs`.
- **Commit:** `feat: slice 10 grouping-engine`.
- **Remaining in phase 3:**
  - 11, persist groups: `FileGroup` and `FileGroupFile` tables. Regroup after each completed scan, replacing only groups that aren't user-confirmed (no confirmed state exists yet, so replace all). Add `GET /api/groups` with list fields, and move the dashboard query into a service with group counts (from slice 08).
  - 12, Incoming screen: lists groups with title, author, file count, duration and confidence, plus the unresolved files. Add `getDashboard` to the `AppLayout` test mock (from slice 09).

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 79 passed, 0 failed | 20 new in `GroupingEngineTests` |
| Frontend tests | 40 passed, 0 failed | |
| New tests | 8 of 8 listed present, passing | Scenarios A–E, unresolved, low confidence, determinism (10 shuffles). Extras: disc-folder name variants, stem and track parsing variants, natural sort, slightly different album tags giving Low |
| Manual | n/a | The slice lists none (pure logic) |

**Data:** none.

## Outcome (2026-10-18, commit eee7aa1)

**Status:** Implemented
**Summary:** This slice adds the pure, static `GroupingEngine.Group` with its models in `Services/Grouping/`.
- **Containers:** the parent folder, or the grandparent for disc, cd or part folders.
- **Grouping:** each M4B is its own book; MP3s group by normalised album plus artist, otherwise by filename stem, otherwise the folder forms one group. Untagged root files that share no stem are unresolved.
- **Order:** disc, then track, then natural sort.
- **Confidence:** counts the signals that agree (single M4B, the whole folder, the same album tag, a shared stem, continuous tag tracks): 2 or more is High, 1 is Medium, and a conflict forces Low.
- **Evidence and estimates:** each group gets evidence strings and an estimated title and author.

Scenarios A–E pass, and so does every other Done when item.
**Changed:** 3 code files plus 3 docs: `Services/Grouping/GroupingEngine.cs`, `Services/Grouping/GroupingModels.cs`, `GroupingEngineTests.cs`.
**Deviations:**
- Untagged MP3s in one folder with different stems (e.g. chapter-titled files) form one folder group at Medium, rather than one group per file. They split by stem only when every file belongs to a series of 2 or more and there are 2 or more series. *Improvement:* a literal reading would have shattered chapter-named books.
- Disc folders are evidence but not a separate signal, so scenario C comes out Medium. *Fine:* the slice left C's confidence open, but the first real scan may show this is too cautious.
- A single M4B with an album tag is High, and the whole folder name is the last fallback for the title. *Fine.*
**Follow-ups:**
- Slice 12 (first real-collection run): review the confidence calibration (C at Medium) and the stem rules against real folders. Tweaks go in a follow-up slice within phase 3.
