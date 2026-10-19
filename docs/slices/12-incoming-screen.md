# Slice 12: Incoming screen

**Phase:** 3, Grouping
**Goal:** a new Incoming page at `/incoming` lists the proposed audiobook groups with title, author, file count, total duration, confidence and evidence, plus the unresolved files. It's reachable from the main nav.

## Done when
- [x] The nav has an "Incoming" link, between Dashboard and Settings, with `aria-current` when active
- [x] `/incoming` shows a table of groups in API order (least confident first). Columns: Title, Author, Files, Duration (`h:mm:ss`), Confidence as text. Each row has an expandable "Evidence" disclosure listing the evidence strings. Missing title or author shows "Unknown"
- [x] An "Unresolved files" section lists each unresolved file's relative path and duration, with a short explanation that these files had too little evidence to group
- [x] Empty and error states: with no groups and no unresolved files, the page says so with a link to the Dashboard to run a scan; a failed load shows a `role="alert"` message
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML; no shadcn component is needed, since `<details>`/`<summary>` gives the disclosure natively), Layout (`pages/`, `types/`, `utils/` with tests), the accessibility rule
- `docs/slices/11-persist-groups.md` Outcome and the `GET /api/groups` shape: `{ groups: [{ id, title|null, author|null, confidence: "Low"|"Medium"|"High", evidence: string[], fileCount, totalDurationMs }], unresolved: [{ fileId, relativePath, durationMs|null }] }`
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and its test, `services/api.ts` and its test, `pages/DashboardPage.tsx` (page style, load and alert pattern), `utils/formatDateTime.ts` (util style)

## Tasks
- [x] **Types, API and duration helper.** Add `src/types/groups.ts` and a `getGroups()` in `services/api.ts`, which throws on a non-200. Add `src/utils/formatDuration.ts`: milliseconds to `h:mm:ss`, with `m:ss` under an hour and "—" for null. Add a test beside it. Verify: the tests below.
- [x] **Incoming page.** Add `src/pages/IncomingPage.tsx` with `<h1>Incoming</h1>`. Load `getGroups()` on mount.
  - Show "Loading…" while loading.
  - The groups section has `<h2>Proposed books (N)</h2>` and a `<table>` with a `<caption>` (visually hidden is fine) and `<th scope="col">` headers. Confidence is plain text, with an optional subtle style per level, never colour alone. Evidence sits in a `<details><summary>Evidence</summary><ul>…</ul></details>` in the last cell.
  - The unresolved section has `<h2>Unresolved files (N)</h2>`, a one-line explanation and a `<ul>` of relative paths with durations.
  - Empty state: "No books found yet." with a link to the Dashboard.
  - A failed load shows the alert.

  Verify: the tests below.
- [x] **Route and nav.** Add the route `/incoming` → `IncomingPage` in `App.tsx`, and an "Incoming" `NavLink` in `AppLayout.tsx`. In `AppLayout.test.tsx`, add `getGroups` and `getDashboard` to the API mock (the slice 09 follow-up) and assert the Incoming link gets `aria-current` at `/incoming`. Verify: the tests below.
- [x] **Tests.** Write the tests below. Verify: `npm test` is green.
- Data: none (reads the slice 11 API).
- Auth: none (no authentication by decision).
- Accessibility:
  - Headings go h1, then h2.
  - The table has a caption and column headers with `scope`.
  - Confidence is text, not just colour.
  - The disclosure is native `<details>`/`<summary>`, so it's keyboard-operable.
  - The empty and error states are text.
  - The alert uses `role="alert"`.
  - The table fits narrow screens: wrap it in an `overflow-x-auto` container so the page itself never scrolls horizontally.

## Tests to add
- `src/utils/formatDuration.test.ts`: 0 gives `0:00`, 61,000 gives `1:01`, 3,723,000 gives `1:02:03`, and null gives "—".
- `src/services/api.test.ts`: `getGroups` returns the parsed body on 200 and throws on 500.
- `src/pages/IncomingPage.test.tsx`: renders the groups in the given order with title, author ("Unknown" when null), file count, formatted duration and confidence text. The evidence strings are inside the row's disclosure.
- `src/pages/IncomingPage.test.tsx`: renders the unresolved files with paths. With both lists empty it shows "No books found yet." and a Dashboard link, and a rejected load shows the alert.
- `src/components/layout/AppLayout.test.tsx`: the Incoming nav link exists and is `aria-current="page"` at `/incoming`. The mock now includes `getGroups` and `getDashboard`.

## Manual checks
- Run the API and client. Lay out temp incoming folders like scenarios A–D plus one stray file (copies of the fixtures, as in slice 11's test run), run a scan from the Dashboard, then open Incoming.
  - 5 groups and 1 unresolved file show, with durations.
  - The evidence expands with Enter or Space on the summary.
  - The nav shows Incoming as current.
  - At a narrow width (about 375 px), the table scrolls inside its container and the page doesn't scroll sideways.
- Delete the temp folders and the dev database afterwards.
- **For the developer, during phase verification:** point incoming at a copy of part of your real collection and check the grouping. Note any book that's split, merged or unresolved wrongly. Those become rule tweaks (slice 10's follow-up) or phase 4 corrections.

## Out of scope
- Group detail and file lists, and any corrections (phase 4).
- Filtering by confidence or status (phase 4 or 5, with the dashboard filters in PRD workflow 8).
- Dashboard group counts. Moved to phase 4, where the dashboard query becomes a service alongside status counts.

## Notes
- **Conflict check:** consistent with AGENTS.md (plain HTML, no new packages, `services/api.ts` as the only caller, utils with tests), the slice 11 API, and phase 3's Done when ("Incoming screen lists groups with title, author, file count, duration, confidence"). It picks up the slice 09 `AppLayout` mock follow-up.
- **Agents never touch real audiobook folders** (AGENTS.md "Don't touch"). The real-collection check is the developer's, during phase verification.
- **Size:** 4 tasks, about 9 files: `groups.ts`, `api.ts` and its test, `formatDuration.ts` and its test, `IncomingPage.tsx` and its test, `App.tsx`, `AppLayout.tsx` and its test.
- **Commit:** `feat: slice 12 incoming-screen`.
- **Phase 3:** this should complete phase 3's Done when. Dashboard group counts move to phase 4's carried items.
