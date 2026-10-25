# Slice 83: Review keyboard shortcuts

**Phase:** 10, Review workspace
**Goal:** in the Review workspace, `j`/`k` move through the books, `a` approves a Ready book, `/` jumps to the catalog search, and `?` opens a help popover that lists them all.

## Done when
- [ ] `j` selects the next book in the shown list (stage filter applied) and `k` the previous one, keeping `?stage`. With no book selected, `j` selects the first and `k` the last; nothing wraps past the ends. Focus moves to the selected book's list link, which scrolls into view.
- [x] `a` presses the selected book's **Approve & next** button when it is shown (Ready books). Otherwise it does nothing.
- [x] `/` focuses the catalog search's Keywords field and doesn't type a "/".
- [x] `?` toggles a **Keyboard shortcuts** popover. The popover is also opened by a visible "Keyboard shortcuts" button beside the Review heading. It lists `j`, `k`, `a`, `/` and `?` with what each does, plus `Enter`'s current meaning ("submit a form or press the focused button"; accepting a highlighted match comes in 84). `Esc` closes it and returns focus to the button.
- [x] No shortcut fires while typing in an `input`, `textarea`, `select` or `contenteditable` element, or with Ctrl, Alt or Meta held. Every shortcut has a visible button or link that does the same thing. All four checks pass.

## Read first
- `AGENTS.md` (Stack: shadcn components, when to add one), `docs/UIUX.md` (Actions and keyboard, Accessibility), `docs/CURRENT-STATE.md`
- `docs/slices/82-approve-and-next.md`
- `src/AudiobookOrganizer.Client/src/pages/ReviewPage.tsx` and `ReviewPage.test.tsx`; `components/review/BookList.tsx` (the shown list, the `Link`s), `components/review/ApproveAction.tsx`, `components/book/CatalogSearch.tsx` (`id="book-keywords"`)
- `src/AudiobookOrganizer.Client/src/components/ui/dropdown-menu.tsx` (how a shadcn component is copied in over `radix-ui`)

## Tasks
- [x] `utils/isTypingTarget.ts` (true for `input`, `textarea`, `select` and `contenteditable` targets) with `utils/isTypingTarget.test.ts`.
- [x] `components/ui/popover.tsx`: shadcn's Popover over `radix-ui`, which is already installed, so no new package. AGENTS.md allows Radix for popovers. `components/review/ShortcutsHelp.tsx`: the trigger button and the list, as a `<dl>` with `<kbd>` keys. Its open state is controlled so `?` can toggle it.
- [x] `pages/ReviewPage.tsx`: one `keydown` listener on `document`, added in an effect and removed on unmount, that ignores typing targets and modified keys. `j`/`k` call `setSearchParams` and focus the link (find it by its `href`, or give `BookList` links a `data-group-id`). `a` clicks `#approve-next` if present, `/` focuses `#book-keywords` with `preventDefault`, `?` toggles the popover. `components/review/ApproveAction.tsx`: give the button `id="approve-next"`.
- [x] Tests (below).
- Data: none
- Auth: none
- Accessibility: shortcuts are single printable keys, so WCAG 2.1.4 applies. They only fire while focus is outside text fields, and only on the Review page, which counts as "active only on focus" in practice. Every action also has a visible control. The popover trigger is a real `<button>` with the visible text "Keyboard shortcuts"; Radix gives the popover its focus handling, `Esc` close and focus return. Keys appear as `<kbd>` with a text description beside each.

## Tests to add
- `utils/isTypingTarget.test.ts`: true for input, textarea, select and a contenteditable div; false for a button, a link and `document.body`.
- `pages/ReviewPage.test.tsx`: `j` from no selection selects the first book; `j` again selects the second and focuses its list link; `k` goes back; `j` on the last shown book (under `?stage=`) stays put. Typing `j` in the Keywords field changes nothing.
- `ReviewPage.test.tsx`: on a Ready book, `a` approves it (calls `approveBook`) and moves on as in slice 82. On a Needs match book, `a` doesn't call `approveBook`. `Ctrl+a` doesn't approve.
- `ReviewPage.test.tsx`: `/` focuses the Keywords field; `?` opens the popover listing the shortcuts, and `Esc` closes it.

## Manual checks
- With the dev data, keyboard only: `j` through a few books (each one's pane loads and its list link stays in view), `k` back, `/` and type keywords (the `j` typed in the field doesn't move), `Esc`/Tab out, then `?` and read the popover, then close it with `Esc`. Do **not** press `a` on a Ready book unless the developer approves processing it.
- With a screen reader or the accessibility tree: the popover's trigger and contents are announced.

## Out of scope
- `Enter` to accept a highlighted match, the highlight itself, and the filled **Accept match** button (84).
- Scan in the header and retiring Dashboard/Incoming/Preview from the nav (85).
- Arrow-key movement inside the list as a single tab stop (UIUX "decided in the slice"): this slice keeps slice 78's list of links, one tab stop per book, and `j`/`k` cover fast movement.

## Notes
- Conflict check: UIUX.md says shortcuts never fire while typing, are listed in a `?` help popover and never replace a visible button. That holds here: `j`/`k` match clicking list links, `a` matches the Approve & next button, `/` matches clicking the Keywords field. The popover is shadcn over Radix, which AGENTS.md allows, and `radix-ui` is already a dependency.
- The UIUX list decision: links, not `role="listbox"`. It's recorded here, and in CURRENT-STATE Decisions at review.
- Split: PLAN.md's "keyboard shortcuts (83)" includes `Enter` to accept the highlighted match, which needs a highlight model in the search results. That goes to **84** with the filled Accept match button, and scan in the header moves to **85**.
- Files: `utils/isTypingTarget.ts`, `utils/isTypingTarget.test.ts`, `components/ui/popover.tsx`, `components/review/ShortcutsHelp.tsx`, `pages/ReviewPage.tsx`, `components/review/ApproveAction.tsx`, `pages/ReviewPage.test.tsx`. 7 files, 4 tasks.

## Test results (2026-10-25)

**Regressions:** none in the suites. **One Done when item failed manually** (below): rapid `j`/`k` presses lost steps; fixed after testing.

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--artifacts-path`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 415 passed, 0 failed | |
| Frontend tests | 333 passed, 0 failed | 20 files; 3 full runs, all green |
| New tests | 4 of 4 present, passing | `isTypingTarget.test.ts` (5); `ReviewPage.test.tsx` shortcuts (7) |
| Manual: `k` with nothing selected | Passed | `/` → `k` selects the last book (Wings of Fire bk 7), focuses its link, list scrolled to it (in view); `j` on the last book stays put |
| Manual: `k`/`j` one at a time | Passed | each press moves one book, pane follows |
| Manual: two quick presses | **Failed** | two `k` presses in a row moved one book (bk 7 → bk 5, not bk 4); two synchronous `j` keydowns from 347 landed on 345, one step. Each keydown computes from the last render's selection, so key repeat (holding `j`) stalls |
| Manual: `/` then typing `j` in Keywords | Passed | focus in Keywords, no "/" typed, `j` typed into the field, selection unchanged |
| Manual: `?` popover | Passed | dialog "Keyboard shortcuts" lists j, k, a, /, Enter, ? with descriptions |
| Manual: `Esc` returns focus to the button | Passed | opened with `?` and with a click: focus moves content → trigger button (focus events logged). A first reading of "body" was a measurement error: it read `activeElement` before Radix returns focus |
| Manual: `a` | Not run | no Ready book in the dev data, and approving processes a real book |
| Manual: screen reader | Not run | accessibility tree only: the popover is a `dialog` with its heading and a `<dl>` of terms and definitions |

Noticed: a Needs match book with a clean preview shows "Ready to approve." under Destination next to "Pick a match or enter the metadata to approve." in the action line, which reads as a contradiction.

**Fix after testing (2026-10-25, developer's OK):** `j`/`k` step from a ref holding the latest selection, so quick presses and key repeat each move one book. New test, seen failing first ("expected '1' to be '2'"): `ReviewPage.test.tsx` "steps once per press even when presses come faster than renders (key repeat)". Browser re-check: three quick `k` from bk 4 → Bk 12 (three steps), three `j` back to bk 4. Destination's clean message is now "No problems with the destination." (developer's choice), so it doesn't contradict a Needs match book's action line. Typecheck clean; build 0 warnings; frontend 334 passed in 2 of 3 runs, the 3rd failed only the accepted `JobsPage` same-title re-focus flake.

## Outcome (2026-10-25, commit 29adfa3; fix 196e2ba)

**Status:** Implemented
**Summary:** The Review page has `j`/`k` (next/previous book in the shown list, focusing and scrolling to its link), `a` (Approve & next on a Ready book), `/` (focus the catalog search) and `?` (a Radix-backed "Keyboard shortcuts" popover, also opened by a visible button). None of them fire while typing or with a modifier. A follow-up fix makes quick or repeated `j`/`k` presses each count, and rewords Destination's clean state.
**Changed:** 8 code files in the feat commit (key: `pages/ReviewPage.tsx`, `components/review/ShortcutsHelp.tsx`, `components/ui/popover.tsx`, `utils/isTypingTarget.ts`, `BookList.tsx` `data-group-id`, `ApproveAction.tsx` id, with tests); 3 in the fix (`ReviewPage.tsx`, `Destination.tsx`, `ReviewPage.test.tsx`)
**Done when:** all 5 met; scroll-into-view confirmed manually.
**Deviations:**
- `BookList.tsx` links gained `data-group-id` (8th file): fine, the slice allowed it.
- `isTypingTarget` also checks the `contenteditable` attribute (jsdom lacks `isContentEditable`): fine.
- Stale selection on quick `j`/`k`: bug, fixed with the developer's OK.
- Destination wording changed from "Ready to approve.": improvement, developer's choice.
- `j`/`k` still fire while the header's More menu is open (not a typing target): debt, minor.
**Follow-ups:** none new. `a` wasn't tried manually (no Ready dev book); its test covers it.
**Decisions:** the review list stays a list of links (one tab stop per book) rather than a `role="listbox"`; `j`/`k` give fast movement (UIUX "decided in the slice").
