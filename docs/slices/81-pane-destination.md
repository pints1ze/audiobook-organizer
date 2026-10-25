# Slice 81: Destination in the review pane

**Phase:** 10, Review workspace
**Goal:** the review pane shows where the selected book will go in the library and any problem that blocks approval, and it updates when the match or metadata changes.

## Done when
- [x] `getBookPreview(groupId)` in `services/api.ts` calls `GET /api/groups/{id}/preview` and returns the `BookPreview` (throws on non-200).
- [x] The Preview page's `fileProblems`/`hasProblems` live in `utils/previewProblems.ts` with a test; `PreviewPage.tsx` imports them and its tests pass unchanged.
- [x] Below Metadata, the pane has a **Destination** section (`h2`): the destination folder (the folder part of the first file's `relativePath`, under the library path), the file count, and a `<details>` "Show all N files" listing each file's `relativePath` (or "No destination"). Every problem (missing tokens, error, duplicate target, already exists) is listed visibly with its file name, outside the `<details>`. A clean preview says "Ready to approve." When the library path isn't set, it says so with a link to Settings.
- [x] The destination reloads after a match pick, a metadata save or a cover save (the pane's `reloadKey`). Loading shows a skeleton line; a failed load shows an inline error.
- [x] All four checks pass.

## Read first
- `AGENTS.md`, `docs/UIUX.md` (Books, Feedback and focus), `docs/CURRENT-STATE.md`
- `docs/slices/80-pane-cover-metadata.md` (the pane's `reloadKey` and `onChanged`)
- `src/AudiobookOrganizer.Client/src/pages/PreviewPage.tsx` (`fileProblems`, `hasProblems`, `FileItem`) and `PreviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/components/review/BookPane.tsx`, `pages/ReviewPage.test.tsx`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getPreview`), `types/` (`BookPreview`, `FilePreview`)
- `src/AudiobookOrganizer.Api/Models/Api/PreviewDtos.cs` (what each field means)

## Tasks
- [x] `services/api.ts`: `getBookPreview(groupId)`; an `api.test.ts` case for 200 and non-200.
- [x] Move `fileProblems` and `hasProblems` from `PreviewPage.tsx` to `utils/previewProblems.ts` (no behaviour change) with `utils/previewProblems.test.ts`; `PreviewPage.tsx` imports them.
- [x] `components/review/Destination.tsx` (`groupId`, `reloadKey`): loads `getBookPreview`, ignoring a response for an older `reloadKey` or book, and renders the section above.
- [x] `components/review/BookPane.tsx`: render `<Destination groupId={book.groupId} reloadKey={reloadKey} />` after Metadata; bump `reloadKey` after a cover or metadata save too (today only a pick bumps it), without reloading Cover or Metadata themselves on their own saves if that resets their status lines. Use a separate key for Destination if needed.
- Data: none (existing `GET /api/groups/{id}/preview`)
- Auth: none
- Accessibility: a `<section>` labelled by its `h2`; problems are text with an `aria-hidden` icon, never colour alone; `<details>`/`<summary>` is natively keyboard-operable; paths use `break-all` so long names wrap; the skeleton is `aria-hidden` with a visually hidden "Loading the destination…" status; the load error is `role="alert"`.

## Tests to add
- `utils/previewProblems.test.ts`: `fileProblems` lists missing tokens, the error, "Duplicate target" and "Already exists"; `hasProblems` is false for a clean book.
- `services/api.test.ts`: `getBookPreview` returns the body on 200 and throws on 404.
- `pages/ReviewPage.test.tsx`: a selected book shows Destination with its folder, "2 files" and "Ready to approve."; a book whose file has a missing token shows "Missing: Series" (with its file name) outside the collapsed list.
- `ReviewPage.test.tsx`: saving metadata re-fetches the book preview and shows the new folder; a failed preview load shows the inline alert.

## Manual checks
- With the dev data, on "Shroud" (book 236): the folder matches the Preview page's entry for the book. Edit the title (then put it back) and the folder follows. A multi-file book shows the count and its files under "Show all".
- A book whose preview has a problem (e.g. "Don't Let Him In", Needs review) shows that problem without expanding anything.

## Out of scope
- **Approve & next** and what happens after it (82); keyboard shortcuts (83); scan in the header (84).
- Changing the naming templates from the pane (Settings).
- Retiring the Preview page (with the nav in 84).

## Notes
- Conflict check: `GET /api/groups/{id}/preview` is the library-wide preview filtered to one book (`NamingPreviewService.Preview`), so its duplicate and existing flags match the Preview page and `POST /api/jobs`'s check (slice 54). UIUX.md: facts that identify the book first; long file lists don't crowd the pane, but problems are always visible.
- Split: PLAN.md's "destination and Approve & next (81)" came to about 10 files with the shared problem helpers, over the cap. This slice is the destination; **82** is Approve & next (button for Ready books, queue, select and focus the next book, announce it); shortcuts move to **83**, scan in the header to **84**.
- Files: `services/api.ts`, `services/api.test.ts`, `utils/previewProblems.ts`, `utils/previewProblems.test.ts`, `pages/PreviewPage.tsx`, `components/review/Destination.tsx`, `components/review/BookPane.tsx`, `pages/ReviewPage.test.tsx`. 8 files, 4 tasks.
