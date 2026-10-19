# Slice 37: Cover picker

**Phase:** 5, Metadata
**Goal:** on the book page the user sees the chosen cover and can choose an embedded cover, the match's cover or an uploaded image instead.

## Done when
- [x] The book page has a "Cover" section (an `h2`, between "Chosen match" and "Metadata") that loads `getCovers(groupId)`. It shows the chosen cover's image (`chosenCoverUrl`, with `?v=<chosenAtUtc>` so a new choice isn't served from the browser cache) and its source ("From <relativePath>", "From the match (<provider name>)" or "Uploaded"), or "No cover chosen yet."
- [x] Each embedded cover is listed with its thumbnail (`fileCoverUrl`, `alt=""`), its file path and a "Use this cover" button; the match's cover (when `provider` isn't null) is listed with its thumbnail and a "Use the match's cover" button. The option that is the chosen one shows "Current cover" instead of a button. No options at all → "This book has no embedded cover and no match with a cover. Upload one below."
- [x] An "Upload a cover" file input (`accept="image/jpeg,image/png"`) with an "Upload" button calls `uploadCover`. Choosing or uploading updates the section from the returned `BookCovers`, announces "Cover saved." in a `role="status"` region and disables the buttons while saving.
- [x] A 400 shows its first message (the upload's `file` error next to the file input via `aria-describedby`), a 502 shows the `downloadFailed` message, a 404 shows "This book no longer exists." with a link back to Incoming, and a thrown error shows "The cover couldn't be saved. Check the connection and try again." All as `role="alert"`. A failed load shows "The covers couldn't be loaded."
- [x] Picking a match reloads the covers, so the match's cover option follows the new match.
- [x] All four checks pass.

## Read first
- `docs/slices/36-cover-upload-api.md`, `## Outcome`
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getCovers`, `chooseCover`, `uploadCover`, `fileCoverUrl`, `chosenCoverUrl`) and `types/metadata.ts` (`BookCovers`, `CoverChoice`, `SaveCoverResult`)
- `src/AudiobookOrganizer.Client/src/components/book/MetadataEditor.tsx` (the load state, `reloadKey`, `mounted` ref, `providerNames`, error and status patterns to copy) and `MetadataEditor.test.tsx`
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`BookContent`, `handlePicked`, `metadataReloadKey`) and `BookPage.test.tsx` (the `vi.mock` of `services/api`)

## Tasks
- [x] **Picker component.** Create `components/book/CoverPicker.tsx` with props `{ groupId, reloadKey }`: load state (loading / loaded / failed) reloaded when either prop changes, the chosen cover, the option list, the upload form and the save handling from Done when. Reuse `providerNames` from `MetadataEditor`. Verify: `CoverPicker.test.tsx`.
- [x] **Book page.** In `pages/BookPage.tsx` render `<CoverPicker groupId={group.id} reloadKey={metadataReloadKey} />` between the chosen match and the metadata editor (picking a match already bumps `metadataReloadKey`). In `BookPage.test.tsx` add `getCovers` to the `vi.mock` and give it a default resolved value so existing tests keep passing. Verify: `BookPage.test.tsx`.
- Data: none.
- Auth: none (single user, no login).
- Accessibility: the section is labelled by its heading; every button names its option (e.g. `aria-label="Use this cover: Disc 1/01.mp3"`); thumbnails are decorative (`alt=""`) since the path or provider is in text, while the chosen cover's image has `alt="Chosen cover"`; the file input has a visible `<label>`; errors are `role="alert"` and linked to the input with `aria-describedby` and `aria-invalid`; after a save focus stays put (the button replaced by "Current cover" moves focus to the section heading, `tabIndex={-1}`). All keyboard-operable with native controls.

## Tests to add
- `src/AudiobookOrganizer.Client/src/components/book/CoverPicker.test.tsx`: shows the chosen embedded cover's image and "From <path>", lists an embedded option and the match's option, and marks the chosen one "Current cover"; with no chosen cover and no options shows both empty messages.
- Same file: "Use the match's cover" calls `chooseCover(4, { source: 'provider' })`, then shows "Cover saved." and the new source; a 502 result shows its `downloadFailed` message as an alert.
- Same file: uploading a selected file calls `uploadCover(4, file)` and updates the section; a 400 result shows the `file` message linked to the input (`aria-invalid`).
- `src/AudiobookOrganizer.Client/src/pages/BookPage.test.tsx`: the page shows the Cover section, and picking a match calls `getCovers` again.

## Manual checks
- Run `scripts/start-api.ps1` and `scripts/start-client.ps1`, open `/books/{id}` for a book whose files have an embedded cover: choose it, then pick a match and choose the match's cover, then upload a JPEG. Each time the chosen image changes without a reload.
- Upload a text file renamed `.png`: the error shows next to the input and the chosen cover is unchanged.
- Tab through the section with the keyboard and check a screen reader reads the buttons with their option and announces "Cover saved."

## Out of scope
- Removing the chosen cover (no "no cover" choice)
- Cropping, resizing or previewing an upload before it's sent
- Embedding the cover into files (phase 8)

## Notes
- File count: 4 (`CoverPicker.tsx`, `CoverPicker.test.tsx`, `BookPage.tsx`, `BookPage.test.tsx`). No API changes.
- The match's thumbnail is the provider URL itself (as the search results already show it); choosing it downloads a copy on the server.
- `?v=<chosenAtUtc>` is a cache-buster on the client only; the server ignores the query.
- This should meet phase 5's last done-when line ("cover can come from the embedded image, a download, or an upload"), so the review should end the phase (gate 5).
- Conflict check: no new package or shadcn component (plain buttons and a native file input), matches the slice 34–36 API and the "Covers can come from the embedded image, a provider download or an upload" decision.
