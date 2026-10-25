# Slice 78: Workspace list

**Phase:** 10, Review workspace
**Goal:** the home page `/` is the review workspace: a list of every unprocessed book with its cover, title, author and stage, filterable by stage, with the selected book in the URL and a placeholder detail pane.

## Done when
- [x] `/` shows the Review page; the Dashboard moves to `/dashboard`. The top bar reads **Review**, Dashboard, Incoming, Preview, Jobs, More.
- [x] The left column lists `GET /api/review`'s books: a 48px cover (chosen → `chosenCoverUrl`, embedded → `fileCoverUrl`, none → a placeholder with the title's initials), title, author (and narrator when set, as "Narrated by …" in muted text), runtime, and a stage chip with its text label.
- [x] Above the list, a filter row: All and each stage with its count (e.g. "Needs match 25"); the filter is in the URL (`?stage=NeedsMatch`) and the empty result says "No books in this stage."
- [x] Choosing a book sets `?book=<groupId>` (keeping `?stage`), marks it in the list (`aria-current="true"` and a visible highlight) and shows the placeholder pane: the book's 160px cover, title, author and an "Open book page" link to `/books/<groupId>`. With no book chosen the pane says "Choose a book from the list." Reloading keeps the selection.
- [ ] At 1024px and wider the list (about 360px, scrolling on its own) and the pane sit side by side; narrower, they stack, and the pane has a "Back to list" link that clears `?book`.
- [x] Loading shows a skeleton list; a failed load shows an alert with a Retry button.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Layout, Books, Accessibility), `docs/CURRENT-STATE.md`
- `docs/slices/77-review-list-api.md` (the response shape)
- `src/AudiobookOrganizer.Client/src/App.tsx`, `components/layout/AppLayout.tsx` and its test
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getGroups`, `chosenCoverUrl`, `fileCoverUrl`) and `services/api.test.ts`
- `src/AudiobookOrganizer.Client/src/pages/JobsPage.tsx` for the existing load/error/announce pattern; `utils/formatDuration.ts`

## Tasks
- [x] Types and API: `types/review.ts` (`ReviewBook`, `ReviewStage`, `ReviewCounts`, `ReviewList`) and `getReview()` in `services/api.ts`, with an `api.test.ts` case.
- [x] `components/review/BookList.tsx`: the filter row (plain `<button>`s with `aria-pressed`), the list (`<ul>` of `Link`s to `?book=`), cover thumbnail, stage chip. Stage chip colours from Tailwind's palette at AA contrast (e.g. `bg-amber-50 text-amber-900`), always with the text label.
- [x] `pages/ReviewPage.tsx` and `ReviewPage.test.tsx`: loads the list, reads and writes `book`/`stage` with `useSearchParams`, renders `BookList` and the placeholder pane, the responsive layout and the "Back to list" link.
- [x] Routing and nav: `App.tsx` (`/` → ReviewPage, `/dashboard` → DashboardPage); `AppLayout.tsx` nav and its test. Check the Dashboard's own "Review on the Incoming page" link and any `to="/"` that meant the dashboard.
- Data: none
- Auth: none
- Accessibility: the list is a `<ul>` of links (one tab stop per book; `j`/`k` arrive in slice 81); the selected link has `aria-current="true"`; thumbnails `alt=""` (the title is beside them); the filter buttons use `aria-pressed` and their count is in the accessible name; the pane is a `<section>` labelled by the book's title heading; the skeleton is `aria-hidden` with a visually hidden "Loading books…" status; the error is `role="alert"`.

## Tests to add
- `pages/ReviewPage.test.tsx`: lists the books with title, author, runtime and stage label; the cover `img` src is the chosen-cover URL, the embedded-cover URL, or no `img` and the initials for a book without a cover.
- `ReviewPage.test.tsx`: choosing "Needs match 2" shows only those books and puts `stage=NeedsMatch` in the URL; an empty stage shows "No books in this stage."
- `ReviewPage.test.tsx`: clicking a book shows its title in the pane and marks it `aria-current`; rendering at `/?book=<id>` selects it on load; a failed load shows the alert and Retry reloads.
- `AppLayout.test.tsx`: nav has Review (`/`) and Dashboard (`/dashboard`).
- `services/api.test.ts`: `getReview` returns the parsed body on 200 and throws otherwise.

## Manual checks
- With the dev data (26 books): the list shows covers where they exist, the filter counts match `GET /api/review`, choosing a book updates the URL and pane, reload keeps it, and at 900px wide the layout stacks with "Back to list".

## Out of scope
- Anything in the detail pane beyond the placeholder: match search and accept (79), cover, metadata, destination and Approve & next (80).
- Keyboard shortcuts (81); the scan header and retiring Dashboard/Incoming/Preview from the nav (82).

## Notes
- Conflict check: matches UIUX.md (covers 48/160px, stage chips with labels, selection in the URL, stacked below 1024px). The Dashboard stays reachable until slice 82 brings scan into the workspace header.
- About 9 files touched (two are small: `App.tsx`, `types/review.ts`); one over the "about 8" guide, kept together because the route, list and page only make sense as one change.
