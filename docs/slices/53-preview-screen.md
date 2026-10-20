# Slice 53: Preview screen

**Phase:** 7, Naming & preview
**Goal:** a Preview page (`/preview`) shows, for every book, each file's source and destination under the current templates, with missing values, errors, duplicate targets and existing files flagged, and moves nothing.

## Done when
- [x] the main nav has "Preview" (between People/Genres and Settings) linking to `/preview`; the page title is "Preview" with a line saying nothing has moved and the paths follow the templates on Settings (linked)
- [x] the page loads `GET /api/preview` and shows the library path and the counts (Files, Missing values, Errors, Duplicate targets, Already exist)
- [x] each book is a section headed by its title (the group title, "Unknown" when empty) linking to `/books/:groupId`, listing its files in order: the source file name with its full source path, and the destination relative path (or "No destination" when there's an error)
- [x] each problem shows as text beside the file, never colour alone: "Missing: series, year", the error message, "Duplicate target", "Already exists"; a "Only show books with problems" checkbox hides books with none, and says so when none are left
- [x] loading, load failure (alert) and empty ("No books to preview yet." with a link to the dashboard) states; all four checks pass

## Read first
- `docs/slices/52-library-preview-api.md` (response shape, Outcome on book titles)
- `src/AudiobookOrganizer.Api/Models/Api/PreviewDtos.cs`
- `src/AudiobookOrganizer.Client/src/pages/IncomingPage.tsx` (list-of-books page with loading and error states) and `pages/DashboardPage.tsx` (counts as a `dl`)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and `AppLayout.test.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getDashboard` pattern)

## Tasks
- [x] **Types and API call.** New `types/preview.ts`: `FilePreview`, `BookPreview`, `PreviewCounts`, `LibraryPreview` mirroring the DTOs (camelCase). `services/api.ts`: `getPreview(): Promise<LibraryPreview>`, throwing on any non-200.
- [x] **Page.** New `pages/PreviewPage.tsx` as in Done when. A file "has problems" when `missing` is non-empty, `error` is set, or `duplicate` or `exists` is true; a book has problems when any file does. Show the file name with `sourcePath.split(/[\\/]/).pop()` and the full path below it in muted text. Use `CircleAlert` (`aria-hidden`) beside problem text. No reload button (navigating back reloads).
- [x] **Route and nav.** `App.tsx`: `<Route path="/preview" element={<PreviewPage />} />`. `components/layout/AppLayout.tsx`: a "Preview" `NavLink` before Settings. `AppLayout.test.tsx`: mock `getPreview` and assert the link if the nav test lists links.
- [x] **Tests.** New `pages/PreviewPage.test.tsx` (below). Run all four checks, keeping full backend output.
- Data: none (client only).
- Auth: none (single user, no login).
- Accessibility: one `h1`, an `h2` per book (its link inside), the counts as a `dl`. Each file is a list item; the source and destination are labelled in text ("From", "To") so the arrow isn't the only cue. The filter is a real `<input type="checkbox">` with a `<label>`, and the "N of M books shown" line is `role="status"`. The load error uses `role="alert"`. Long paths wrap (`break-all`) instead of overflowing at 320 px.

## Tests to add
- `PreviewPage.test.tsx`: renders the counts and two books in order with their files' "From" and "To" paths; a book title links to `/books/{id}`; an empty title reads "Unknown".
- `PreviewPage.test.tsx`: a file with `missing: ['series', 'year']`, one with `duplicate`, one with `exists` and one with an `error` and null paths show "Missing: series, year", "Duplicate target", "Already exists", the error text and "No destination".
- `PreviewPage.test.tsx`: ticking "Only show books with problems" hides the clean book and the status reads "1 of 2 books shown"; with no problem books it says none have problems.
- `PreviewPage.test.tsx`: an empty `books` list shows "No books to preview yet."; a rejected `getPreview` shows the alert.
- `AppLayout.test.tsx`: the nav has a "Preview" link to `/preview`.

## Manual checks
- With the API and client running and a few scanned books, open `/preview`: counts match the books; change the file template on `/settings` to `%album%` and back to `/preview`: multi-file books show "Duplicate target". Restore the template.
- Put an empty file at one destination in the library folder: that file shows "Already exists". Delete it afterwards. Confirm nothing else appeared in the library.
- At 320 px wide, long paths wrap; tab through the page: book links, then the checkbox, in a sensible order.

## Out of scope
- Approving or processing books, and choosing what to do about conflicts (phase 8)
- Per-book preview on the book page (the endpoint exists; add it if missed)
- Paging, sorting or searching the preview

## Notes
- File count: 7 (`types/preview.ts`, `services/api.ts`, new `PreviewPage.tsx`, new `PreviewPage.test.tsx`, `App.tsx`, `AppLayout.tsx`, `AppLayout.test.tsx`). Under the cap.
- Why next: the last piece of phase 7's done-when ("the preview screen shows source → destination per file and flags missing values, duplicate targets and existing-file conflicts").
- Book titles use the group title (as Incoming shows), resolving slice 52's open point: the destination path already shows the metadata title, so the heading matches the name the user knows from Incoming.
- No shadcn component: a checkbox, lists and headings are plain HTML (AGENTS.md stack rule).
- Conflict check: read-only screen; consistent with overview.md "Screen 4: Organization Preview" and PRD workflow 6. No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 337 passed, 0 failed | Unchanged (client-only slice). Full output kept (TRX + console) |
| Frontend tests | 249 passed, 0 failed | Was 239; `PreviewPage` + `AppLayout` files: 14 passed |
| New tests | 5 of 5 present, passing | `PreviewPage.test.tsx` (7): counts and books with From/To, title link and "Unknown"; missing/duplicate/exists/error with "No destination"; problems filter and its status lines; empty state; load alert. `AppLayout.test.tsx`: Preview in nav order and current on `/preview`. Plus 2 `getPreview` tests in `api.test.ts` |
| Manual: duplicates after a `%album%` file template | Not run | API and client not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: an existing library file shows "Already exists"; library otherwise untouched | Not run | Same |
| Manual: 320 px wrapping and tab order | Not run | Same |

Data checks: none (client only).

## Outcome (2026-10-19, commit c95aa1c)

**Status:** Implemented
**Summary:** The Preview page (`/preview`, in the main nav before Settings) loads `GET /api/preview` and shows the library path and counts, then each book (group title linking to its book page) with every file's From and To paths and its problems as text: missing values, errors, "Duplicate target", "Already exists". A checkbox shows only books with problems. Nothing is moved.
**Changed:** 8 files: new `types/preview.ts`, `pages/PreviewPage.tsx`, `pages/PreviewPage.test.tsx` (7); `services/api.ts` (`getPreview`), `services/api.test.ts` (+2), `App.tsx` (route), `AppLayout.tsx` (nav link), `AppLayout.test.tsx` (nav order, current link).
**Deviations:**
- An 8th file, `api.test.ts`, with two `getPreview` tests following `getDashboard`'s: *improvement*.
- The library path is the first row of the counts list, with "Not set." and a Settings link like the dashboard: *fine*.
- The filter status always reads "N of M books shown" (and "No books have problems." when the filter empties the list): *fine*.
**Follow-ups:**
- Phase 7's done-when lines are met. Developer verification at the phase end should include the manual checks carried from slices 45–53 (the book page People/Genres sections, genres editing, the preview GETs and this screen).
- Phase 8 starts from this preview: approving books, never overwriting `exists` destinations and refusing `duplicate` targets are processing's job.
