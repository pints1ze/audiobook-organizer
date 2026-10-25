# Slice 80: Cover and metadata in the review pane

**Phase:** 10, Review workspace
**Goal:** the review pane lets the user choose the book's cover and edit its metadata (with the match's proposed values beside them) without leaving the workspace, and the list follows those changes.

## Done when
- [x] Below the Match section, the pane shows the Book page's **Cover** section (`CoverPicker`) and **Metadata** section (`MetadataEditor`) for the selected book, with the chosen match passed to the editor so its proposed values show as on the Book page.
- [x] Picking a match reloads both sections (the match cover and proposed values follow it), as on the Book page.
- [x] Saving a cover or saving metadata reloads the review list in the background (as slice 79 does after a pick): a newly chosen cover appears as the book's list thumbnail and pane cover, and a book given user-entered values with no match moves from Needs match to Ready or Needs review.
- [x] The pane's headings have no level skips: the book title, Match, Catalog search, Cover and Metadata are all `h2` inside the pane; result and match titles stay `h3` (fixes slice 79's heading-order debt). Focus after a pick still goes to the Match heading.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Feedback and focus, Accessibility), `docs/CURRENT-STATE.md`
- `docs/slices/79-pane-match-search.md` (Outcome: the heading debt)
- `src/AudiobookOrganizer.Client/src/components/review/BookPane.tsx`, `pages/ReviewPage.tsx` and `pages/ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`BookContent`: how `metadataReloadKey`, `CoverPicker` and `MetadataEditor` are wired)
- `src/AudiobookOrganizer.Client/src/components/book/CoverPicker.tsx` and its test; `components/book/MetadataEditor.tsx` (`onSaved`)

## Tasks
- [x] `components/book/CoverPicker.tsx`: add an optional `onSaved?: () => void` prop, called after a choose or upload saves (as `MetadataEditor`'s `onSaved`). No other change; the Book page doesn't pass it. Add a `CoverPicker.test.tsx` case.
- [x] `components/review/BookPane.tsx`: render `CoverPicker` and `MetadataEditor` after the Match section, with a `reloadKey` bumped on a pick and `match={currentMatch}`. Rename the `onMatchSaved` prop to `onChanged` and call it after a pick, a cover save and a metadata save. Make the Match heading an `h2` (still `tabIndex={-1}`, still the focus target after a pick).
- [x] `pages/ReviewPage.tsx`: pass `onChanged={() => void reload()}`.
- [x] Tests (below) in `pages/ReviewPage.test.tsx`; update its mocks so the pane's `getCovers` and `getMetadata` calls resolve, and its Match heading assertion to level 2.
- Data: none (existing cover and metadata endpoints)
- Auth: none
- Accessibility: both sections keep their own labels, status lines, `role="alert"` errors and focus handling from the Book page; the pane's headings go title `h2` → section `h2`s with no skips. One `CoverPicker` and one `MetadataEditor` are on the page at a time, so their fixed ids (`cover-heading`, `cover-file`, `metadata-heading`, `metadata-*`) stay unique.

## Tests to add
- `components/book/CoverPicker.test.tsx`: choosing a cover calls `onSaved` once; a failed save doesn't call it.
- `pages/ReviewPage.test.tsx`: selecting a book shows the Cover and Metadata sections for it; choosing a cover re-fetches `getReview` and the list thumbnail's `src` is the chosen-cover URL.
- `ReviewPage.test.tsx`: saving metadata for a Needs match book re-fetches `getReview` and its chip reads the new stage.
- `ReviewPage.test.tsx`: picking a match re-fetches the covers and metadata (the editor shows the match's proposed value).
- `ReviewPage.test.tsx`: the pane has no heading level above 3 below its `h2`s and no `h3` directly under the book title (title, Match, Catalog search, Cover, Metadata are `h2`).

## Manual checks
- With the dev data, on "Shroud" (book 236, matched in slice 79): choose the match cover; the pane and list thumbnail change (watch for the browser serving the old `/cover/image` from cache). Edit one metadata field and save; the status reads "Metadata saved." and focus is on the Metadata heading.
- On a Needs match book, enter a title by hand and save: the chip moves to Ready or Needs review.
- Keyboard only through Cover and Metadata in the pane: every control reachable, focus visible.

## Out of scope
- Destination and **Approve & next** (81); keyboard shortcuts (82); scan in the header and retiring Dashboard/Incoming/Preview from the nav (83).
- People and Genres links (stay on the Book page; "Open book page" reaches them).
- Showing only *changed* metadata as a diff: the editor already shows current vs the match's proposed values; a compact diff can come with Approve in 81 if the pane is too long.

## Notes
- Conflict check: matches PLAN.md (phase 10: cover and metadata in the pane) and UIUX.md (status beside the control, focus to the section heading). Reuses the Book page components unchanged except `CoverPicker`'s optional `onSaved`, a second caller of the same shape as `MetadataEditor`'s.
- Split: PLAN.md's "cover, metadata, destination and Approve & next (80)" was over the cap (5 tasks / ~8 files) once the per-book destination preview client and Approve were counted. This slice keeps cover and metadata; **81** is the destination (per-book preview from slice 51, needs a `services/api.ts` function) and **Approve & next** (`approveBook`, then select the next book in the list and focus it); shortcuts move to **82**, scan in the header to **83**.
- Risk: `chosenCoverUrl` is the same URL before and after a cover change, so the browser may show the old image. If the manual check shows that, a follow-up adds a version to the URL; not fixed speculatively here.
- Files: `CoverPicker.tsx`, `CoverPicker.test.tsx`, `BookPane.tsx`, `ReviewPage.tsx`, `ReviewPage.test.tsx`. 5 files, 4 tasks.
