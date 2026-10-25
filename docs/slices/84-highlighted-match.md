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

## Test results (2026-10-25)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--artifacts-path`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 415 passed in 3 of 4 runs | 1st run: 1 unnamed failure, then 3 clean reruns; no backend change since slice 80's fix, so treated as the accepted unidentified intermittent backend failure (slice 49) |
| Frontend tests | 339 passed, 0 failed | 20 files; 3 full runs, all green |
| New tests | 4 of 4 present, passing | 4 new `BookPage.test.tsx` cases (focus + filled first, skip current match, arrows/ends + pick, no results keeps focus), 1 `ReviewPage.test.tsx` (`/`, search, Enter accepts, Match heading; popover Enter line); "Use this match" queries renamed |
| Manual: review pane keyboard flow | Passed | book 337 "The Priory of the Orange Tree": `/` → Enter → focus on "Accept match: The Priory of the Orange Tree, result 1 of 7" (only filled button); ArrowDown → result 2 highlighted and filled, page didn't scroll; ArrowUp → back; Enter → "Match saved: The Priory of the Orange Tree", focus on the Match heading, chip → Ready |
| Manual: Book page | Passed | `/books/337` search: result 1 is "Current match", focus lands on result 2 "A Day of Fallen Night" |

**Dev data changed:** book 337 "The Priory of the Orange Tree" now has its correct Audible match (Samantha Shannon, Liyah Summers, 25:52:00 vs 25:52:45).

## Outcome (2026-10-25, commit 59dbfc5)

**Status:** Implemented
**Summary:** Catalog search results now have one highlighted **Accept match** button (filled, row bordered and shaded). It takes focus after a search, skipping the current match. Arrow keys move the highlight, and `Enter` accepts natively, so `/`, type, `Enter`, `Enter` matches a book. This applies on the review pane and the Book page.
**Changed:** 4 code files (`components/book/CatalogSearch.tsx`, `components/review/ShortcutsHelp.tsx`, `pages/BookPage.test.tsx`, `pages/ReviewPage.test.tsx`)
**Done when:** all 5 met.
**Deviations:**
- Result rows are bordered boxes instead of divided by a bottom border: fine, it lets the highlight show as a border change, not colour alone.
- If accepting fails, focus is lost because the pressed button is disabled during the save (pre-existing since slice 37's search): debt; move focus to the error alert in a later polish.
**Follow-ups:** focus the pick error on a failed accept (polish, after phase 10).
