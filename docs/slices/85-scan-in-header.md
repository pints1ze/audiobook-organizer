# Slice 85: Scan in the workspace header

**Phase:** 10, Review workspace
**Goal:** the user starts a scan from the Review page's header and sees the last scan there. The list reloads when a scan finishes, and the top bar is trimmed to Review, Jobs and More.

## Done when
- [x] Beside the Review heading (with the Keyboard shortcuts button) there is a **Scan incoming** outline button and a status line:
  - With no scan yet: "No scans yet."
  - While a scan runs: "Scanning… N files found", updated by polling `GET /api/scans/latest` as the Dashboard does.
  - When it ends: "Last scan <date/time>: N files added" (`formatDateTime`), or "Last scan failed: <error>".
- [x] When a scan the user started (or one already running on load) completes, the review list reloads in the background. The book selection and stage filter are kept, and the status line announces "Scan completed" or "Scan failed: …".
- [x] While a scan runs, the button is `aria-disabled` (not `disabled`, so focus stays on it) and a second press does nothing. A refused start (409, already running) shows the API's message, and a failed request shows an alert. With no incoming path set, the button links the user to Settings instead.
- [x] The top bar reads **Review**, **Jobs**, **More**. More holds Incoming, Preview, Dashboard, People, Genres and Settings, and all their routes still work. `AppLayout.test.tsx` covers it.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Layout, Feedback and focus), `docs/CURRENT-STATE.md` (Decisions: the developer's nav and scan choices, 2026-10-25)
- `src/AudiobookOrganizer.Client/src/pages/DashboardPage.tsx` (`poll`, `beginPolling`, `handleScan`, `LastScan`: the pattern to follow) and `DashboardPage.test.tsx` (how polling is tested with fake timers)
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.tsx` (`reload`), `ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/components/layout/AppLayout.tsx` and its test
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getLatestScan`, `startScan`, `getSettings`), `types/dashboard.ts` (`ScanRun`)

## Tasks
- [x] `components/review/ScanControl.tsx` (`onCompleted()`): loads `getLatestScan()` and `getSettings()` (for the incoming path) on mount, then the button, status line and polling described above. It resumes polling if the latest scan is already running, and clears its timer on unmount. Follow the Dashboard's logic rather than sharing code with it: the Dashboard stays as it is.
- [x] `pages/ReviewPage.tsx`: render `<ScanControl onCompleted={() => void reload()} />` in the header row.
- [x] `components/layout/AppLayout.tsx`: main links Review and Jobs; the More menu gets Incoming, Preview and Dashboard first, then People, Genres, Settings. Update `AppLayout.test.tsx`.
- [x] Tests (below) in `pages/ReviewPage.test.tsx` (mock `getLatestScan`, `startScan`, `getSettings`; fake timers for polling).
- Data: none (existing `POST /api/scans`, `GET /api/scans/latest`, `GET /api/settings`)
- Auth: none
- Accessibility: the button is a real `<button>` with visible text. `aria-disabled` while busy, so focus isn't lost. The status line is `role="status"` beside the button, and errors are `role="alert"`. The More menu stays the Radix dropdown from slice 76, keyboard-operable, and its items are links.

## Tests to add
- `pages/ReviewPage.test.tsx`: on load with a completed last scan, the header shows "Last scan … : 3 files added"; with none, "No scans yet."
- `ReviewPage.test.tsx`: pressing Scan incoming calls `startScan` and shows "Scanning… 2 files found". When the poll returns `Completed`, it announces "Scan completed", `getReview` is fetched again and `?book=` is unchanged. A second press while running doesn't call `startScan` again.
- `ReviewPage.test.tsx`: a 409 start shows its message; a running scan on load resumes polling; a failed scan shows "Scan failed: <error>".
- `components/layout/AppLayout.test.tsx`: the main nav has exactly Review and Jobs, and the More menu lists Incoming, Preview, Dashboard, People, Genres and Settings with their routes.

## Manual checks
- With the dev data and the dev API: press Scan incoming on the Review page. The status shows progress and then the last-scan line, the list reloads with the same selection, and the counts match `GET /api/review`. A scan is read-only, and slices 24/67 make rescans safe.
- Keyboard: Tab to Scan incoming, press it, and check that focus stays on it while it runs. Open More with the keyboard and reach Incoming.

## Out of scope
- Removing the Dashboard, Incoming or Preview pages (they stay under More; developer, 2026-10-25).
- Background identification after a scan (phase 11).
- A per-scan "N new books" count. `ScanRun` counts files, so the line says files added.

## Notes
- Conflict check: the developer chose "Move to More" and "Button + last scan" on 2026-10-25. UIUX.md's top bar (Review, Jobs, More) is met, with Incoming, Preview and Dashboard added to More, so the grouping tools on Incoming stay reachable. The scan button is outline because the pane's filled button is reserved for its primary action.
- Polling logic is duplicated from `DashboardPage`, not shared. The Dashboard keeps its own, the two may diverge once the Dashboard is under More, and extracting a hook for two callers can wait. Mark it with `// shortcut: polling mirrors DashboardPage — revisit if a third caller appears`.
- This is the last slice in PLAN.md's phase 10 list. After its review, phase 10's done-when lines are checked for the phase end (gate 5).
- Files: `components/review/ScanControl.tsx` (new), `pages/ReviewPage.tsx`, `pages/ReviewPage.test.tsx`, `components/layout/AppLayout.tsx`, `components/layout/AppLayout.test.tsx`. 5 files, 4 tasks.

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--artifacts-path`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 415 passed, 0 failed | |
| Frontend tests | 349 passed, 0 failed | 20 files; 3 full runs, all green |
| New tests | 4 of 4 present, passing | 9 new `ReviewPage.test.tsx` cases (last scan / none, start + poll + reload keeping `?book`, second press ignored, 409, resume on load, failed scan); `AppLayout.test.tsx` nav and More items |
| Manual: top bar | Passed | main nav reads Review, Jobs, More |
| Manual: scan from the Review header | Passed | before: "Last scan Oct 25, 2026, 7:55 AM: 507 files added"; Enter on Scan incoming → "Scan completed. Last scan Oct 25, 2026, 10:37 AM: 0 files added", `?book=337` and its pane kept, counts (All 125, Needs match 123, Needs review 1, Ready 1) = `GET /api/review`. The scan finished within one poll, so "Scanning…" wasn't seen |
| Manual: keyboard | Passed | focus stayed on Scan incoming throughout; Enter on More opened the menu with focus on Incoming (Incoming, Preview, Dashboard, People, Genres, Settings); Enter → `/incoming` |

## Outcome (2026-10-25, commit 1fa6eca)

**Status:** Implemented
**Summary:** The Review header has a Scan incoming button with a last-scan status line. It polls while a scan runs and reloads the list, keeping the selection, when the scan completes. The top bar is Review, Jobs, More, with Incoming, Preview and Dashboard moved under More.
**Changed:** 5 code files (`components/review/ScanControl.tsx` new, `pages/ReviewPage.tsx`, `ReviewPage.test.tsx`, `components/layout/AppLayout.tsx`, `AppLayout.test.tsx`)
**Done when:** all 5 met.
**Deviations:**
- The "Loading books…" assertion now uses `getByText` (the page has a second status) and two AppLayout tests expect More to be current for Dashboard/Preview: fine, same behaviour checked.
- Alerts for a failed last-scan load and a failed poll, as on the Dashboard: improvement.
- The completed line reads "Scan completed. Last scan …" and a failed scan doesn't reload the list: fine.
- Polling duplicated from `DashboardPage` (marked shortcut): debt, as planned.
**Follow-ups:** none.
