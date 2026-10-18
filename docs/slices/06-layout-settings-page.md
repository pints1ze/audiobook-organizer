# Slice 06: Layout shell and settings page

**Phase:** 2, Settings & scan
**Goal:** the client gets a layout shell with navigation and a not-found page, plus a Settings page. The page loads the directory settings, saves them, and shows the API's validation errors beside the right fields.

## Done when
- [x] Every page renders inside a layout with a header (app name), nav links "Dashboard" (`/`) and "Settings" (`/settings`) with `aria-current="page"` on the active one, and a skip link to the main content
- [x] An unknown route like `/nope` shows a "Page not found" page with a link back to the dashboard
- [x] `/settings` shows the saved incoming path, library path and "Keep source files after processing" toggle. Saving valid values shows "Settings saved." Saving invalid values shows each API error message under its field and moves focus to the first invalid field
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (React built-ins plus `services/api.ts` only; shadcn components only when Radix behaviour is needed, so plain HTML here), Layout (`pages/`, `components/layout/`, `types/`), the accessibility rule (WCAG 2.1 AA)
- `docs/slices/05-directory-settings-api.md`: the API contract. `GET /api/settings` returns `{incomingPath, libraryPath, keepSourceFiles}` (paths may be null). `PUT` returns 200 with the saved DTO, or 400 `ValidationProblemDetails` with `errors.incomingPath` / `errors.libraryPath` (string arrays; overlap errors land on `libraryPath`)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `services/api.ts`, `services/api.test.ts`, `pages/DashboardPage.tsx`, `pages/DashboardPage.test.tsx`, `index.css`

## Tasks
- [x] **API client and types.** Add `src/types/settings.ts` (`Settings`, `SettingsFieldErrors`). In `services/api.ts`, add `getSettings()` and `saveSettings(settings)`. `saveSettings` returns `{ ok: true, settings }` for a 200, or `{ ok: false, errors }` for a 400 (from `errors` in the problem body), and throws on anything else. Verify: the api tests below.
- [x] **Layout and routing.** Add `src/components/layout/AppLayout.tsx`:
  - a skip link ("Skip to main content", visible on focus) that targets `<main id="main">`
  - a `<header>` with the app name "Audiobook Organizer" as a link to `/`
  - a `<nav aria-label="Main">` using `NavLink` for Dashboard and Settings
  - `<main id="main">` with `<Outlet />`

  Add `src/pages/NotFoundPage.tsx` (`<h1>Page not found</h1>` plus a link to the dashboard). Restructure `App.tsx` to export an `AppRoutes` component: the layout route wrapping `/` → `DashboardPage`, `/settings` → `SettingsPage`, and `*` → `NotFoundPage`. `App` wraps `AppRoutes` in `BrowserRouter`, so tests can use `MemoryRouter`. Change the dashboard's `<h1>` to "Dashboard", since the app name moves to the header, and update its test. Verify: the routing tests below.
- [x] **Settings page.** Add `src/pages/SettingsPage.tsx`:
  - On mount, load the settings. Show "Loading settings…" while loading, and an error message in `role="alert"` if the load fails.
  - The form has labelled text inputs for "Incoming directory" and "Library directory" (with a hint like "Full path, e.g. /incoming in Docker"), a labelled checkbox "Keep source files after processing", and a "Save" button. The button is disabled while saving.
  - On submit, call `saveSettings`. Success: show "Settings saved." in `role="status"` and refresh the fields from the response.
  - Validation failure: set `aria-invalid="true"` on each failing input, show its messages under it (linked with `aria-describedby`), and focus the first invalid field. Any other failure shows a `role="alert"` message, and the user's input is kept, not cleared.

  Verify: the tests below and the manual check.
- [x] **Tests.** Write the tests below. Verify: `npm test` is green.
- Data: none (uses the slice 05 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Every input has a visible `<label>`, and hints and errors are tied to their input with `aria-describedby`.
  - Errors are text, not just colour.
  - Focus moves to the first invalid field on a failed save.
  - The skip link is the first focusable element.
  - Nav has an `aria-label`, and the active link uses `aria-current`.
  - Visible focus styles are on links, inputs and the button.
  - Contrast stays on the existing AA tokens.

## Tests to add
- `src/services/api.test.ts`: `saveSettings` returns `{ok:true}` with the settings on a 200 and `{ok:false, errors}` on a 400 problem body, and throws on a 500.
- `src/components/layout/AppLayout.test.tsx` (rendering `AppRoutes` in a `MemoryRouter`): `/settings` marks the Settings nav link `aria-current="page"`, and `/nope` renders "Page not found" with a link to `/`.
- `src/pages/SettingsPage.test.tsx`: loads and shows saved values; a successful save shows "Settings saved.".
- `src/pages/SettingsPage.test.tsx`: a 400 result shows the library error under the library field, sets `aria-invalid`, and focuses that field.
- `src/pages/DashboardPage.test.tsx`: updated for the "Dashboard" heading (existing tests otherwise unchanged).

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, then open http://localhost:5173/settings.
  - Save two real, separate folders and see "Settings saved.". Reload the page and the values are still there.
  - Point the library inside the incoming folder. The library field shows "The library directory can't be inside the incoming directory." and gets focus.
- Keyboard only: Tab from the address bar. The skip link appears first; activating it jumps to main. Tab reaches the nav, the inputs, the checkbox and Save, each with a visible focus ring.
- Visit http://localhost:5173/nope and see "Page not found".
- Afterwards, reset the dev database settings to real folders or delete `data/audiobooks.db` (slice 05 left stale values there). Report which.

## Out of scope
- A folder picker or browse dialog (paths are typed).
- Naming templates and provider settings (phases 5 and 7).
- Dark mode, responsive nav menu, shadcn components.
- Scan button and counts on the dashboard: later phase 2 slices.

## Notes
- **Conflict check:** consistent with AGENTS.md (plain HTML, no new packages, `services/api.ts` as the only backend caller) and with the slice 05 API contract. It picks up the slice 03 follow-up (layout shell, not-found route).
- **Size:** 4 tasks, about 11 files. New: `settings.ts`, `AppLayout.tsx` + test, `NotFoundPage.tsx`, `SettingsPage.tsx` + test. Changed: `api.ts` + test, `App.tsx`, `DashboardPage.tsx` + test. That's above the ~8 guide, but 4 of the files are tests beside their pages and 2 are one-line heading changes. Splitting off the layout would leave a slice with nothing to navigate to.
- **Commit:** `feat: slice 06 layout-settings-page`.
- **Remaining in phase 2:**
  - 07 scan inventory. Backend: `AudioFile` and `ScanRun` tables; a recursive MP3/M4B scan of the configured incoming directory using TagLibSharp for tags, duration and cover presence, plus `ChapterReader`; unchanged files (same path, size and mtime) skipped on rescan; unreadable files recorded, not fatal; read-only.
  - 08 scan API and dashboard. `POST /api/scans` runs in the background, plus a status endpoint; the dashboard shows the configured directories, last scan time and counts, with a "Scan incoming" button.

## Test results (2026-10-18)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors |
| Typecheck | Passed | `npx tsc -b --noEmit` |
| Backend tests | 40 passed, 0 failed | |
| Frontend tests | 20 passed, 0 failed | 4 files |
| New tests | 5 of 5 behaviours present, passing | api `saveSettings`; `AppLayout` aria-current and not-found; `SettingsPage` load, save, 400 focus; dashboard heading |
| Manual: settings load and valid save | Passed | Playwright on :5173. Saved values loaded; Save shows "Settings saved." in `role="status"` |
| Manual: library inside incoming | Passed | Focus moves to "Library directory" with `aria-invalid="true"`. `aria-describedby` reads the hint plus "The library directory can't be inside the incoming directory." |
| Manual: keyboard | Passed | The first Tab lands on a visible skip link; Enter moves focus to `main#main`. Order: skip, home, Dashboard, Settings (`aria-current`), Incoming, Library, checkbox, Save. Focus outline is solid |
| Manual: `/nope` | Passed | "Page not found" h1 with a "Back to the dashboard" link |

**Data:** deleted the stale dev database `data/audiobooks.db` (git-ignored, test values only). It's recreated empty on the next API start.

## Outcome (2026-10-18, commit 3738ec4)

**Status:** Implemented
**Summary:** This slice adds the layout shell: skip link, header home link, `nav aria-label="Main"` with `NavLink` and `aria-current`, and `<main id="main">`. It adds a not-found route and the Settings page, which loads and saves through `getSettings` and `saveSettings`. The page shows field-level API errors with `aria-invalid`, `aria-describedby` and focus on the first invalid field, and the "Settings saved." status. All Done when items are met.
**Changed:** 12 code files plus 2 docs. Key ones: `components/layout/AppLayout.tsx`, `pages/SettingsPage.tsx`, `pages/NotFoundPage.tsx`, `App.tsx` (`AppRoutes`), `services/api.ts`.
**Deviations:**
- The dashboard's own `<main>` was removed, since the layout provides it, and `main` has `tabIndex={-1}` as the skip-link target. *Fine.*
- A global `:focus-visible` outline rule was added to `index.css` using the existing token. *Improvement*: it covers the visible-focus requirement.
- The status region is always rendered, so announcements are reliable. *Improvement.*
- A 400 without field errors falls back to the general alert. *Improvement.*
- The hint wording differs from the slice. *Fine.*
**Follow-ups:** none new.
