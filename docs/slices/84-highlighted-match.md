# Slice 84: Highlighted match and Enter to accept

**Phase:** 10, Review workspace
**Goal:** after a catalog search, one result is highlighted with a filled **Accept match** button that has focus, so `/`, type, `Enter` (search), `Enter` (accept) matches a book without the mouse.

## Done when
- [x] Every result's button reads **Accept match**. Its accessible name is "Accept match: <title>, result i of n", replacing "Use this match". The highlighted result has a visible highlight (a border and background on its row, not colour alone: its button is filled while the others are outline). Exactly one result is highlighted.
- [x] When a search returns results, focus moves to the first result that can be accepted (skipping one marked "Current match"), and that result is highlighted. With no results, or after an error, focus stays in Keywords as today.
- [x] With focus on a result's button, `ArrowDown`/`ArrowUp` move focus and the highlight to the next/previous acceptable result, stopping at the ends, and the list doesn't scroll the page by itself. Focusing a result's button any other way (Tab, click) highlights it too. `Enter` on the focused button accepts it (native button behaviour). After that, slice 79's flow applies: "Match saved", focus on the Match heading.
- [x] The shortcuts popover's `Enter` line reads "Accept the highlighted match (in the search results)". Both the review pane and the Book page get this behaviour, since they share `CatalogSearch`.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Actions and keyboard, Accessibility), `docs/CURRENT-STATE.md`
- `docs/slices/83-review-shortcuts.md` (Outcome), `docs/slices/79-pane-match-search.md`
- `src/AudiobookOrganizer.Client/src/components/book/CatalogSearch.tsx` (`handleSubmit`, `CandidateItem`), `components/review/ShortcutsHelp.tsx`
- `src/AudiobookOrganizer.Client/src/pages/BookPage.test.tsx` and `pages/ReviewPage.test.tsx` (they query "Use this match")

## Tasks
- [x] `components/book/CatalogSearch.tsx`: a `highlighted` index (reset on each search to the first acceptable result) and refs to the result buttons. After a search with results, focus that button. `onKeyDown` on the results list handles ArrowDown/ArrowUp with `preventDefault`, and `onFocus` on a button sets the highlight. Relabel the button to "Accept match", filled when highlighted, outline otherwise, and give the row a highlight style.
- [x] `pages/BookPage.test.tsx` and `pages/ReviewPage.test.tsx`: update the "Use this match" queries to "Accept match". The behaviour they test is unchanged, so this weakens nothing. Then the new tests (below).
- [x] `components/review/ShortcutsHelp.tsx`: the `Enter` line.
- Data: none
- Auth: none
- Accessibility: focus moving to the first result after a search lands on a button whose name gives the title and "result 1 of n". The `role="status"` "N matches found." stays. Arrow keys only act while focus is on a result button, so they don't interfere with page scrolling elsewhere. The highlight is a border and background plus the filled button, never colour alone. Every result stays reachable with Tab.

## Tests to add
- `pages/BookPage.test.tsx`: after a search, focus is on "Accept match: <first title>, result 1 of 2", which is filled; the second is outline. When the first result is the current match, focus goes to the second.
- `BookPage.test.tsx`: `ArrowDown` from the first result's button focuses and highlights the second, `ArrowDown` on the last stays put, and `ArrowUp` goes back. `Enter`/click on the focused one calls `pickMatch` with that candidate.
- `BookPage.test.tsx`: a search with no results leaves focus in Keywords.
- `pages/ReviewPage.test.tsx`: in the pane, `/`, type, submit, then pressing the focused Accept match saves the match ("Match saved: …") and focus lands on the Match heading. The popover's `Enter` line reads "Accept the highlighted match (in the search results)".

## Manual checks
- With the dev data, keyboard only on a Needs match book in the review pane: `/`, type keywords, `Enter`, check that focus is on the first result, `ArrowDown`/`ArrowUp`, then `Enter` on the right one. Accepting saves a real match on a dev book, so only accept a correct match (or ask the developer).
- The same flow on the Book page (`/books/<id>`).

## Out of scope
- Accepting a match with a global `Enter` while focus is elsewhere (e.g. on a list link after `j`/`k`). There, `Enter` keeps its native meaning; `/` gets you to the search.
- Scan in the header and retiring Dashboard/Incoming/Preview from the nav (85).
- Stored and scored candidates (phase 11).

## Notes
- Conflict check: UIUX.md says "`Enter` accept the highlighted match", "Accept match when matching" as the filled primary action, and that shortcuts never replace a visible button. Here `Enter` is the native press of the visible, focused, highlighted Accept match button, so no global listener is needed and it can't fire while typing. On a Ready book that is being re-searched, the highlighted Accept match and Approve & next are both filled. In that state the user is matching, so this is accepted rather than adding a prop to `CatalogSearch`.
- Moving focus out of Keywords after a search changes the Book page's flow too; slice 79 made the two share one component on purpose.
- Files: `components/book/CatalogSearch.tsx`, `pages/BookPage.test.tsx`, `pages/ReviewPage.test.tsx`, `components/review/ShortcutsHelp.tsx`. 4 files, 3 tasks.
