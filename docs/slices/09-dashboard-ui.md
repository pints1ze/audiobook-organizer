# Slice 09: Dashboard with scan button and progress

**Phase:** 2, Settings & scan
**Goal:** the dashboard shows the configured directories, the last scan and the inventory counts. A "Scan incoming" button starts a scan, the page shows progress while it runs, and it refreshes when the scan finishes.

## Done when
- [x] The dashboard shows:
  - the incoming and library paths, or "Not set" with a link to Settings
  - the last scan's status, finish time (local time) and counts (found, added, updated, unchanged, missing, failed), plus the error text when it failed
  - "Audio files" and "Files with errors" counts, labelled as coming from the last completed scan
- [x] "Scan incoming" starts a scan. It's disabled while one runs and when incoming isn't set. A 409 shows the API's message
- [x] While a scan is `Running`, including one found already running on page load, the page polls `GET /api/scans/latest` every second and shows "Scanning… N files found" in a `role="status"` region. When the scan ends, it reloads the dashboard and announces "Scan completed" or "Scan failed: {error}"
- [x] The existing API and database health lines still show
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (React built-ins plus `services/api.ts`, no data-fetching libraries; plain HTML), Layout (`types/`, `utils/` helpers each with a test), the accessibility rule
- `docs/slices/08-scan-api.md`, including its Outcome. The API shapes:
  - `POST /api/scans` returns 202 with a ScanRun, or 409 ProblemDetails (`title`, `detail`).
  - `GET /api/scans/latest` returns 200 with a ScanRun, or 204.
  - `GET /api/dashboard` returns `{ incomingPath, libraryPath, lastScan: ScanRun|null, audioFileCount, filesWithErrors }`.
  - A ScanRun is `{ id, startedAt, finishedAt|null, status: "Running"|"Completed"|"Failed", filesFound, filesAdded, filesUpdated, filesUnchanged, filesMissing, filesFailed, error|null }`.
  - The counts come from the latest *completed* run. `lastScan` may be a `Running` one.
- `src/AudiobookOrganizer.Client/src/pages/DashboardPage.tsx` and its test, `services/api.ts` and its test, `types/settings.ts` (style)

## Tasks
- [x] **Types and API client.** Add `src/types/dashboard.ts` (`ScanRun`, `ScanStatus`, `Dashboard`). In `services/api.ts`, add:
  - `getDashboard()`
  - `getLatestScan()`: `ScanRun | null` (`null` on 204)
  - `startScan()`: `{ started: true, run }` on 202, or `{ started: false, message }` on 409, using `detail` or else `title`. It throws otherwise.

  Verify: the api tests below.
- [x] **Date helper.** Add `src/utils/formatDateTime.ts`, which turns an ISO UTC string into a short local date and time with `Intl.DateTimeFormat` (no library), and returns "—" for null. Add a test beside it. Verify: its test.
- [x] **Dashboard page.** Rework `DashboardPage.tsx`, keeping the health lines in their own small section. Load `getDashboard()` on mount and show three sections, each with an `<h2>`:
  - "Directories", as a definition list.
  - "Last scan": status, finished time, counts as a definition list, and the error.
  - "Inventory", with the note "From the last completed scan".

  Add a "Scan incoming" `<button>`. On click it calls `startScan()`, then polls. Polling is a `setTimeout` loop of 1 second, cleaned up on unmount, that calls `getLatestScan()` until the status isn't `Running`, then reloads the dashboard. If `lastScan` is `Running` on load, start polling at once. A failed load shows a `role="alert"` message; a failed poll shows an alert and stops polling. Verify: the tests below and the manual check.
- [x] **Tests.** Write the tests below. Use `vi.useFakeTimers()` for polling, and mock `services/api`. Verify: `npm test` is green.
- Data: none (reads the slice 08 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Headings go h1, then h2 per section.
  - Counts use `<dl>` with `<dt>`/`<dd>`.
  - The button has a visible label, and its disabled state uses the `disabled` attribute plus a visible reason (e.g. "Set the incoming directory in Settings first.") tied to it with `aria-describedby`.
  - Progress and completion are announced through `role="status"` (polite).
  - The failure message is text, not just colour.
  - Focus stays on the button after starting a scan; it's not moved.

## Tests to add
- `src/services/api.test.ts`: `getLatestScan` returns null on 204. `startScan` returns `started: true` on 202 and `started: false` with the problem `detail` on 409.
- `src/utils/formatDateTime.test.ts`: it formats an ISO string to a non-empty local string and returns "—" for null.
- `src/pages/DashboardPage.test.tsx`: shows the directories, last scan counts and inventory counts from a mocked dashboard. With `incomingPath: null`, it shows "Not set" with a Settings link and the scan button is disabled.
- `src/pages/DashboardPage.test.tsx`: clicking "Scan incoming" calls `startScan`. While polling returns `Running`, the status shows "Scanning… 5 files found". When it returns `Completed`, the dashboard reloads and "Scan completed" is shown. Use fake timers.
- `src/pages/DashboardPage.test.tsx`: a 409 result shows "Wait for the current scan to finish…". A `Running` `lastScan` on load starts polling without a click. The existing health tests still pass, adjusted to the new layout.

## Manual checks
- Run the API and client scripts, save real folders in Settings, and copy a handful of audiobooks (or the fixtures) into incoming. Open http://localhost:5173.
  - The directories show, and "Last scan" is empty.
  - Click "Scan incoming". The status shows scanning, then "Scan completed", and the counts appear.
  - Click again and see the unchanged counts.
  - Clear incoming in the database, or use a fresh database. The button is disabled with the reason.
- Keyboard: Tab to "Scan incoming" and press Enter. Focus stays on it, and the status is announced. Check this with a screen reader if one is handy; otherwise check the `role="status"` text changes.
- Afterwards, delete the test folders and the dev database. Report it.

## Out of scope
- Book groups, statuses and confidence filters (phase 3 onward).
- Cancelling a scan.
- Scan history.
- Live updates without polling (SSE or websockets).

## Notes
- **Conflict check:** consistent with AGENTS.md (plain HTML, no new packages, `services/api.ts` as the only backend caller, utils with tests), the slice 08 contract and the slice 08 Outcome (show completed-run counts during a scan). Polling every second is light, a single SQLite read, and stops when the scan ends.
- **Size:** 4 tasks, 7 files: `dashboard.ts`, `api.ts` and its test, `formatDateTime.ts` and its test, `DashboardPage.tsx` and its test.
- **Commit:** `feat: slice 09 dashboard-ui`.
- **Phase 2:** this should complete phase 2's Done when. The tag round trip (04), validated settings (05/06), scan with rescan skip (07/08) and dashboard (09) are all covered.
