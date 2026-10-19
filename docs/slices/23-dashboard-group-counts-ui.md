# Slice 23: Show book and file-state counts on the Dashboard

**Phase:** 4, Group corrections
**Goal:** the Dashboard page gets a "Books" section with the counts slice 22 added to `GET /api/dashboard`: proposed books, how many are Low, Medium and High confidence, how many the user corrected, and how many files are unresolved or excluded. It links to the Incoming page to review them.

## Done when
- [x] `types/dashboard.ts` adds to `Dashboard`: `bookCount`, `booksByConfidence: { low; medium; high }`, `correctedBookCount`, `unresolvedFileCount` and `excludedFileCount`
- [x] Under Inventory, a labelled "Books" section (`h2`) shows a `<dl>`: "Proposed books", "Low confidence", "Medium confidence", "High confidence", "Corrected by you", "Unresolved files" and "Excluded files", each with its number. A "Review on the Incoming page" link goes to `/incoming`
- [x] The counts refresh when a scan completes, the same way the Inventory counts do today
- [x] All four checks pass

## Read first
- `AGENTS.md`: Stack (plain HTML; `react-router-dom` `Link`), the accessibility rule
- `docs/slices/22-dashboard-group-counts-api.md` Outcome: the new fields and their JSON names
- `src/AudiobookOrganizer.Client/src/pages/DashboardPage.tsx` (the Inventory section and the reload after a scan) and its test, `types/dashboard.ts`, `components/layout/AppLayout.test.tsx` (its `getDashboard` mock), `services/api.test.ts` (its dashboard fixture)

## Tasks
- [x] **Type.** Add the fields to `Dashboard`. Update the dashboard fixtures in `DashboardPage.test.tsx`, `AppLayout.test.tsx` and `api.test.ts` so they typecheck, without changing their existing values. Verify: `npx tsc -b --noEmit`.
- [x] **Books section.** In `DashboardPage.tsx`, add the section after Inventory, in the same `<dl>` grid style, with the `Link`. Reuse the existing reload after a scan; add no new fetch. Verify: the page tests below.
- [x] **Tests.** Write the tests below. Verify: `npm test` is green.
- Data: none.
- Auth: none (no authentication by decision).
- Accessibility:
  - The section has an `h2` and `aria-labelledby`, like its neighbours.
  - Each count is a `dt`/`dd` pair, so the label is read with the number.
  - Confidence is named in words, not colour.
  - The link text says where it goes.
  - Nothing scrolls sideways at 375 px.

## Tests to add
- `src/pages/DashboardPage.test.tsx`: with a dashboard of 5 books (1 low, 1 medium, 3 high), 2 corrected, 4 unresolved and 1 excluded, the "Books" section shows each label with its number, and the link points to `/incoming`.
- `src/pages/DashboardPage.test.tsx`: after a scan completes, the reloaded dashboard's new `bookCount` is shown. Extend the existing reload test, keeping its assertions.

## Manual checks
- Run the API against a scratch database (`Database__Path`, not the dev `data/audiobooks.db`) and the client. Lay out scenario A (three tracks), one M4B and one stray root MP3 (copies of the fixtures), and scan from the Dashboard.
  - After the scan, Books shows 2 proposed (0 low, 1 medium, 1 high), 0 corrected, 1 unresolved and 0 excluded, without a page reload.
  - Exclude the stray file on Incoming, then return to the Dashboard: 0 unresolved, 1 excluded.
  - At about 375 px wide, the page doesn't scroll sideways.
- Delete the scratch folders and database afterwards.

## Out of scope
- Processing statuses and their counts (phases 7 and 8).
- Filters by status and confidence (with the processing statuses).
- Linking each count to a filtered Incoming list.

## Notes
- **Conflict check:** consistent with PLAN.md phase 4 (reopened for "dashboard group and status counts"), the PRD dashboard, the slice 22 contract and AGENTS.md (plain HTML, no new packages). No `[NEEDS CLARIFICATION]` in scope.
- **Size:** 3 tasks, 5 files: `types/dashboard.ts`, `DashboardPage.tsx` and its test, `AppLayout.test.tsx`, `api.test.ts`.
- **Commit:** `feat: slice 23 dashboard-group-counts-ui`.
