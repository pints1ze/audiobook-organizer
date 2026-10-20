# Slice 45: Templates in settings

**Phase:** 7, Naming & preview
**Goal:** the directory and file naming templates are stored with the other settings, validated through `NamingTemplate.Render`, have the agreed defaults, and can be edited on the Settings page.

## Done when
- [x] `GET /api/settings` returns `directoryTemplate` and `fileTemplate`; with nothing stored they are the defaults `%artist%/[%series%/Book %series-part:2% - ]%album%` and `[%track:3%. ]%album% (%artist%)`
- [x] `PUT /api/settings` stores both templates with the paths and toggle in one save; a blank template, a template `Render` rejects (unknown token, unbalanced `[ ]`, …) or a file template containing `/` or `\` is a 400 with the error under `directoryTemplate` or `fileTemplate`, and nothing is saved
- [x] the Settings page shows both templates with a hint listing the nine tokens, saves them with the rest of the form, and shows and focuses a template's error like the path fields
- [x] all four checks pass

## Read first
- `docs/CURRENT-STATE.md` decisions 2026-10-18 (template syntax) and 2026-10-19 (developer: the default file and directory templates), and slice 44's Outcome
- `src/AudiobookOrganizer.Api/Services/Naming/NamingTemplate.cs`: `Tokens`, `Render` and its `TemplateResult`
- `src/AudiobookOrganizer.Api/Services/SettingsService.cs`, `Data/SettingsRepository.cs`, `Models/Api/SettingsDto.cs`, `Controllers/SettingsController.cs`
- `src/AudiobookOrganizer.Client/src/pages/SettingsPage.tsx` and its test, `src/types/settings.ts`

## Tasks
- [x] **DTO and storage.** `Models/Api/SettingsDto.cs`: add `string? DirectoryTemplate, string? FileTemplate` (after `LibraryPath`, before `KeepSourceFiles`, or wherever keeps call sites simplest). `Data/SettingsRepository.cs`: add keys `DirectoryTemplate` and `FileTemplate`. Rows live in the existing `ApplicationSetting` table.
- [x] **Defaults and validation.** `Services/SettingsService.cs`: `public const string DefaultDirectoryTemplate` and `DefaultFileTemplate` with the values above. `Get()` returns the default for a template with no stored row. `Save` trims both templates and validates each (error keys `directoryTemplate`, `fileTemplate`, matching the existing camelCase convention):
  - blank → `"Enter the directory template."` / `"Enter the file template."`
  - otherwise call `NamingTemplate.Render(template, sample)` where `sample` gives every name in `NamingTemplate.Tokens` a non-empty value (e.g. `"1"` for track, disc, series-part and year, `"x"` for the rest), so only structural errors come back; a non-null `Error` is the field error as is
  - the file template contains `/` or `\` → `"The file template can't contain folders; put them in the directory template."`
  - template errors are checked alongside the path errors, so one save reports all of them; nothing is stored unless all pass. All five values go in the same `SetMany` call.
- [x] **Backend tests.** Extend `Tests/Services/SettingsServiceTests.cs` and `Tests/Controllers/SettingsControllerTests.cs` (below). Existing tests that build a `SettingsDto` or PUT a body now pass valid templates.
- [x] **Client.** `src/types/settings.ts`: add `directoryTemplate: string | null` and `fileTemplate: string | null` to `Settings`, and both keys to `SettingsFieldErrors`. `SettingsPage.tsx`: two more text fields using the existing `PathField` (rename it to `TextField` if the name now misleads; it's local to the file), labelled "Directory template" and "File template". Hint: "Tokens: %artist% %album% %series% %series-part% %track% %disc% %title% %year% %narrator%. Pad with %track:3%; wrap optional parts in [ ]." (the file hint adds "The extension is added for you."). Load, save and reset them from the response like the paths; error focus order is incoming, library, directory template, file template. `services/api.ts` needs no change (it sends the whole `Settings`).
- [x] **Client tests.** Extend `SettingsPage.test.tsx` (below), then run all four checks.
- Data: two new `ApplicationSetting` keys, written by `Save`; no schema change. Existing databases have no rows, so `Get` serves the defaults until the next save.
- Auth: none (single user, no login).
- Accessibility: each template input has a visible `<label>`, the hint via `aria-describedby`, `aria-invalid` and its error list on failure, and gets focus when it's the first field in error, same as the path fields. `spellCheck={false}` and `autoComplete="off"`.

## Tests to add
- `SettingsServiceTests.cs`: with nothing stored, `Get` returns both defaults; a valid save round-trips both templates (trimmed).
- `SettingsServiceTests.cs` (a `[Theory]`): directory template `""`, `%author%/%album%` and `[%series%` and file template `%artist%/%album%` each fail under the right key and store nothing (a later `Get` still shows the previous values); a template error and a path error in one save are both reported.
- `SettingsControllerTests.cs`: `GET` on a fresh database includes the two defaults; `PUT` with an unknown token returns 400 with `errors.directoryTemplate`.
- `SettingsPage.test.tsx`: the template fields load from `getSettings` and are sent by `saveSettings`; a 400 with `fileTemplate` shows its error and focuses that input.

## Manual checks
- Open `/settings` with the API running: an existing dev database shows the two default templates. Change the file template to `%track:3% %title%`, save, reload: the change sticks. Enter `%author%` and save: the error shows under the field and focus moves to it. Restore the default.

## Out of scope
- A live example path under the template fields (the preview screen covers it)
- Mapping a book's fields to tokens, the preview endpoint and screen
- Linking book values to canonical entries (next slices)
- A "reset to default" button

## Notes
- File count: 8 (`SettingsDto.cs`, `SettingsRepository.cs`, `SettingsService.cs`, `SettingsServiceTests.cs`, `SettingsControllerTests.cs`, `types/settings.ts`, `SettingsPage.tsx`, `SettingsPage.test.tsx`). At the cap; `SettingsController.cs` and `api.ts` don't change.
- Why next: it's the next piece in PLAN.md's phase 7 line, and the preview needs stored templates. The default file template was decided by the developer on 2026-10-19 (with ` (%artist%)`).
- Templates are required on PUT (a missing one is "Enter the … template."), not silently defaulted, so a client can't wipe one by omission. The client always sends them.
- Validating with a full sample keeps `Missing` and the empty-path case out of settings: those depend on a book, so they belong to the preview.
- Conflict check: matches the syntax decision, PRD workflow 1 ("the directory and file naming templates") and the PRD's `ApplicationSetting` entity. No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 311 passed, 0 failed | Was 301 |
| Frontend tests | 217 passed, 0 failed | Was 215; 13 files |
| New tests | 4 of 4 present, passing | `SettingsServiceTests.cs`: defaults with nothing stored, trimmed round-trip, invalid-template theory (blank, unknown token, unbalanced `[`, `/` and `\` in file template), template and path errors together. `SettingsControllerTests.cs`: GET defaults, PUT unknown token → 400 `directoryTemplate`. `SettingsPage.test.tsx`: templates load and save, `fileTemplate` error shown and focused |
| Manual: `/settings` defaults, save, reload, `%author%` error and focus | Not run | API and client weren't running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |

Data checks (for the developer, not run): `SELECT Key, Value FROM ApplicationSetting WHERE Key IN ('DirectoryTemplate','FileTemplate');` shows no rows before the first save and the saved values after.

## Outcome (2026-10-19, commit e8c5228)

**Status:** Implemented
**Summary:** `GET`/`PUT /api/settings` now carry `directoryTemplate` and `fileTemplate`, stored as `ApplicationSetting` rows and served as the agreed defaults when none are stored. `Save` rejects a blank template, any structural `Render` error (checked against sample values for every token) and folders in the file template, reporting them with the path errors. The Settings page edits both with a token hint, the same error display and the extended focus order.
**Changed:** 13 files: `SettingsDto.cs`, `SettingsRepository.cs`, `SettingsService.cs`, `types/settings.ts`, `SettingsPage.tsx` (`PathField` renamed `TextField`, a `show(settings)` helper), plus tests in `SettingsServiceTests.cs`, `SettingsControllerTests.cs` and `SettingsPage.test.tsx`.
**Deviations:**
- 13 files touched instead of 8: `ScansControllerTests.cs`, `GroupingServiceTests.cs`, `ScanServiceTests.cs`, `api.test.ts` and `AppLayout.test.tsx` only gained template values in their settings fixtures, which the slice allowed ("Existing tests … now pass valid templates") but didn't count: *fine*. The next writer should count fixture files when a DTO gains required fields.
- More tests than listed (blank and whitespace file template, `\` in the file template, exact messages, directory-before-file focus): *fine*.
**Follow-ups:**
- Manual `/settings` check not run (servers were down); carried to the phase 7 verification.
- Next: linking a book's authors, narrators and genres to canonical entries (API first).
