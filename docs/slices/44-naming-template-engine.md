# Slice 44: Naming template engine

**Phase:** 7, Naming & preview
**Goal:** a pure engine turns a naming template and a book's token values into a sanitized relative path that can never escape the library folder, and reports which values were missing.

## Done when
- [x] `NamingTemplate.Render(template, values)` substitutes the nine PRD tokens (`%artist% %album% %series% %series-part% %track% %disc% %title% %year% %narrator%`), pads with `%token:N%` and drops `[optional]` sections whose tokens are empty
- [x] a required (non-optional) token with no value renders empty and is listed in `Missing`; unknown tokens, a malformed `%`, unbalanced or nested `[ ]` and a `.`/`..` path segment give an `Error` and no `Path`
- [x] every path segment is sanitized the same way on Windows and Linux (illegal characters, control characters, trailing dots and spaces, reserved device names, empty segments, length); a `/` or `\` inside a token value never creates a folder
- [x] `NamingTemplate.ResolveUnder(root, relativePath)` returns the full path and refuses (returns null for) anything that would land outside `root`, on both Windows and Linux path styles
- [x] the series-aware and single-file examples from `overview.md` render exactly, as tests
- [x] all four checks pass

## Read first
- `docs/PRD.md` (the "Templates support…" rule and workflow 6, Preview) and `overview.md` lines 455–520 (tokens, examples, preview)
- `docs/CURRENT-STATE.md` decision 2026-10-18: template syntax is `%token%`, `%token:N%` padding and `[optional]` sections only
- `src/AudiobookOrganizer.Api/Services/Grouping/GroupingEngine.cs` and `GroupingModels.cs`: the style for a pure static engine with `[GeneratedRegex]` and record results
- `src/AudiobookOrganizer.Api.Tests/Services/GroupingEngineTests.cs`: the style for pure engine tests

## Tasks
- [x] **Models.** `Services/Naming/NamingModels.cs`: `public record TemplateResult(string? Path, IReadOnlyList<string> Missing, string? Error)`. `Path` is relative, with `/` between segments, and has no extension (the caller adds it). `Missing` lists token names (`"series-part"`) without `%`, in template order, with no duplicates.
- [x] **Parse and render.** `Services/Naming/NamingTemplate.cs`, `public static partial class NamingTemplate` with `Render(string template, IReadOnlyDictionary<string, string?> values)`. Keys are token names without `%`, ordinal and case-insensitive; a missing key counts as empty, and so does a whitespace-only value.
  - `%name%` and `%name:N%` (N is 1–9). Padding left-pads the leading integer part with zeros (`7` → `007`, `1.5` with `:2` → `01.5`); a value with no leading digits is left as is.
  - `[ … ]` is kept, without its brackets, when every token inside has a value, and dropped completely when any token inside is empty. Tokens inside a dropped section don't count as `Missing`. Text in a section with no tokens is always kept.
  - Errors (no `Path`, `Missing` empty): an unknown token name, a `%` with no closing `%`, a bad padding width, `[` without `]` or `]` without `[`, a nested `[`, a blank template, and a template whose literal text has a `.` or `..` segment.
- [x] **Sanitize.** In the same class: split the rendered text into segments on the template's literal `/` and `\` only. Token values are cleaned *before* substitution: `/`, `\` and the other illegal characters become `-`, so a value never adds a folder. Then each segment:
  - replace `< > : " | ? *` and control characters (U+0000–U+001F) with `-` (one fixed set on every OS, because the library may be read from Windows over SMB)
  - collapse runs of whitespace to one space, then trim spaces and trailing dots
  - apply Unicode NFC
  - append `_` to Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, also with an extension, case-insensitive)
  - cut to 200 characters, without splitting a surrogate pair. Mark it `// shortcut: char cap, not a 255-byte UTF-8 cap — revisit when a real name hits a filesystem limit`.
  - Drop segments that end up empty. If nothing is left, return `Error = "The template produced an empty path."`
- [x] **Resolve under root.** `public static string? ResolveUnder(string root, string relativePath)`: refuse a rooted `relativePath` (`Path.IsPathRooted`, or starting with `/` or `\`, or a drive letter); combine with `Path.GetFullPath(Path.Combine(root, …))`; return null unless the result is under `Path.GetFullPath(root)` plus a separator (compared case-insensitively on Windows, ordinally on Linux, using `OperatingSystem.IsWindows()`). This is the guard the preview and processing slices call; `Render` output passes it by construction, but the check doesn't rely on that.
- [x] **Tests.** Add `src/AudiobookOrganizer.Api.Tests/Services/NamingTemplateTests.cs` with the tests below and run all four checks.
- Data: none (pure code, nothing stored; templates in settings come in a later slice).
- Auth: none (single user, no login).
- Accessibility: none (no UI).

## Tests to add
- `NamingTemplateTests.cs`: the overview examples. `%artist%/[%series%/Book %series-part:2% - ]%album%` + file `[%track:3%. ]%album%` with Brandon Sanderson / Mistborn / 1 / The Final Empire / track 1 gives `Brandon Sanderson/Mistborn/Book 01 - The Final Empire` and `001. The Final Empire`. With no series, the dir gives `Ernest Cline/Ready Player One`, and the file template `[%track:3%. ]%album% (%artist%)` with no track gives `Ready Player One (Ernest Cline)`. Neither lists `Missing`.
- Padding and missing values: `%track:3%` with `7`, `%series-part:2%` with `1.5`, a non-numeric value left alone; a required `%narrator%` with no value lists `narrator` in `Missing` and still renders a `Path`; a token inside a dropped section isn't listed.
- Template errors (a `[Theory]`): an unknown token `%author%`, a lone `%`, `%track:0%`, `[a`, `a]`, `[[%series%]]`, `../%album%`, `%artist%/./x` and `""` each give an `Error` and a null `Path`.
- Sanitizing (a `[Theory]`): `AC/DC` as artist gives one segment `AC-DC`; `What? Why: Now*` gives `What- Why- Now-`; `Dr. Who...` loses its trailing dots; `CON` and `nul.txt` get `_`; a 300-character title is cut to 200; a token value of `..` trims to empty and its segment is dropped (it never becomes a parent reference). A template that renders only empty segments gives the empty-path error.
- `ResolveUnder`: a normal relative path under a temp root returns a full path inside it; `../x`, `a/../../x`, `/etc/x`, `C:\x` and `\\server\x` return null; on Windows a root `C:\Lib` doesn't accept `C:\Library\x` (prefix without a separator).

## Manual checks
- None needed beyond the suite: the engine has no UI or I/O. Run `dotnet test src/AudiobookOrganizer.Api.Tests --filter NamingTemplate` once on Windows. The Linux path rules are covered when phase 9 runs the suite in the Docker image.

## Out of scope
- Storing directory and file templates in settings, the defaults and editing them on the Settings page (next slice)
- Mapping a book's `AudiobookField` values and its files' track and disc to tokens, including how lists (several authors or narrators) are joined
- The preview endpoint and screen, duplicate targets and existing-file conflicts
- Linking book values to canonical people and genres (open question, see Notes)
- Mp3tag functions (`$if`, `$num`, …), which are out of scope for v1 in the PRD

## Notes
- File count: 3 (`Services/Naming/NamingModels.cs`, `Services/Naming/NamingTemplate.cs`, `Tests/Services/NamingTemplateTests.cs`). Under the cap.
- Why next: it's the first line of phase 7's done-when and has no dependencies. The preview needs it, and the settings slice needs it to validate templates.
- No nesting for `[ ]` keeps the parser one level deep. The overview's series example works without it, because a missing series part drops the whole series section. Revisit only if a real template needs nesting.
- Proposed defaults for the settings slice: directory `%artist%/[%series%/Book %series-part:2% - ]%album%`, file `[%track:3%. ]%album%`. They reproduce the overview's series-aware example. For the single-file example `Ready Player One (Ernest Cline).m4b`, the file template would also need ` (%artist%)`. The settings slice should pick one, and that's the developer's call.
- `%title%` vs `%album%`: here, `album` is the book title and `title` is the per-file title. The mapping slice settles this; the engine only substitutes.
- Still open for the developer (gate 4, from slice 43): linking a book's authors, narrators and genres to canonical entries (the `normalized` origin) before naming and tagging. It isn't in phase 7's done-when, so it needs a PLAN.md change if it should go here. This slice doesn't depend on it.
- Conflict check: matches the 2026-10-18 syntax decision, the PRD's "sanitized for the platform and can never escape the destination directory", and AGENTS.md's "code must handle both Windows and Linux paths". No new packages: `System.Text` normalization and `[GeneratedRegex]` are in the framework.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 301 passed, 0 failed | Was 243; `--filter NamingTemplate` gives 58 passed |
| Frontend tests | 215 passed, 0 failed | Unchanged (13 files, no client changes) |
| New tests | 5 of 5 present, passing | `NamingTemplateTests.cs`: overview examples (series-aware, single-file), padding and missing values, template errors theory, sanitizing theory plus NFC, 200-character cut, surrogate pair and empty path, `ResolveUnder` (inside root, trailing separator, outside-root theory, sibling prefix, `Render` output always resolves) |
| Manual: `--filter NamingTemplate` on Windows | Passed | 58 passed |
| Linux path rules | Not run | Deferred to phase 9's suite run inside the Docker image |

Data checks: none (nothing stored).

## Outcome (2026-10-19, commit 0d6fa32)

**Status:** Implemented
**Summary:** `NamingTemplate.Render` turns a template and token values into a sanitized relative path, using the nine PRD tokens, `%token:N%` padding and one-level `[optional]` sections, and reports `Missing` tokens or a template `Error`. `NamingTemplate.ResolveUnder` returns a full path under the root, or null for anything rooted, outside the root, or the root itself. The overview's series-aware and single-file examples render exactly.
**Changed:** 3 files: `Services/Naming/NamingModels.cs`, `Services/Naming/NamingTemplate.cs` (new), `Tests/Services/NamingTemplateTests.cs` (58 tests). Plus this slice file, `docs/PLAN.md` and `docs/CURRENT-STATE.md`.
**Deviations:**
- Reserved names get `_` after the stem (`nul.txt` → `nul_.txt`, not `nul.txt_`), because Windows treats everything before the first dot as the device name: *improvement*.
- Token names in templates are case-insensitive (`%ARTIST%`), values are trimmed, a public `Tokens` list exposes the nine names (for settings validation and the UI), and `ResolveUnder` also refuses the root itself: *fine*.
- More tests than listed (extra error cases, NFC, surrogate pairs, `CONSOLE` untouched, a trailing-separator root, and a property that `Render` output always resolves). The sibling-prefix test uses temp roots, so it runs on every OS: *fine*.
**Follow-ups:**
- Next slice: directory and file templates in settings (defaults, validation through `Render`, Settings page fields). The developer picks the default file template (with or without ` (%artist%)`, see Notes).
- Then: mapping a book's fields and its files' track and disc to tokens (how lists are joined, `%title%` vs `%album%`), then the preview endpoint and screen.
- The Linux path rules are covered only by the phase 9 Docker suite run.
