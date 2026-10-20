# Slice 50: Book genre links UI

**Phase:** 7, Naming & preview
**Goal:** the book page shows each genre as linked to a canonical genre or not, and lets the user link an unresolved genre to a suggested one or add it as a new genre, using the same component as the People section.

## Done when
- [x] the book page has a "Genres" section after "People", listing the book's genres from `GET /api/groups/{id}/genres` in order: "Normalized: {genre}" when resolved, otherwise "Not linked" with its suggestions and reasons
- [x] "Link to {genre}" adds the name as that genre's alias (`PUT /api/genres/{id}` with its name and aliases plus the name); "Add as new genre" creates it (`POST /api/genres` with `{ name, aliases: [] }`); after either, the section reloads and announces "{name} linked to {genre}."
- [x] failures show on the row as in People ("That genre no longer exists." for a 404); a book with no genres shows "No genres yet."; the Genres section reloads after a metadata save
- [x] the People section behaves exactly as before (its existing tests pass unchanged apart from the component's new name and `kind` prop)
- [x] all four checks pass

## Read first
- `docs/slices/47-book-people-links-ui.md` (the People section this generalizes) and `48-book-genres-api.md` (response shape)
- `src/AudiobookOrganizer.Client/src/components/book/PeopleLinks.tsx` and `PeopleLinks.test.tsx`
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`peopleReloadKey`) and `pages/BookPage.test.tsx` (mocks, heading-order assertions)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`getBookPeople`) and `types/names.ts` (`NameKind`, `NameLink`)

## Tasks
- [x] **Types and API call.** `types/names.ts`: `BookGenres { groupId: number; genres: NameLink[] }`. `services/api.ts`: `getBookGenres(groupId): Promise<BookGenres>`, throwing on any non-200 like `getBookPeople`.
- [x] **Generalize the component.** `git mv` `components/book/PeopleLinks.tsx` → `NameLinks.tsx` and its test → `NameLinks.test.tsx`. Add a `kind: NameKind` prop. A small per-kind table holds what differs: heading ("People" / "Genres", `id` `people-heading` / `genres-heading`), the loader (`getBookPeople` → Authors and Narrators lists; `getBookGenres` → one list, shown without an `h3`), the empty text ("No authors or narrators yet." / "No genres yet."), the add label ("Add as new person" / "Add as new genre", aria "Add {name} as a new person|genre") and the 404 text ("That person|genre no longer exists."). Link and add call `updateEntry(kind, …)` / `createEntry(kind, …)`. Everything else (busy state, focus, status, row errors) stays shared.
- [x] **Book page.** `pages/BookPage.tsx`: render `<NameLinks kind="people" …>` then `<NameLinks kind="genres" …>` after the metadata editor. Rename `peopleReloadKey` to `namesReloadKey` and pass it to both (a pick bumps it too; genres don't change on a pick, but one key is simpler than two).
- [x] **Tests.** Existing people tests in `NameLinks.test.tsx` render with `kind="people"`; add the genre tests below. `BookPage.test.tsx`: mock `getBookGenres` and add "Genres" to the heading-order assertions. Run all four checks.
- Data: none (client only).
- Auth: none (single user, no login).
- Accessibility: the Genres section has its own `h2` with `aria-labelledby`; with one list there's no `h3`, so the list is labelled by the section. Buttons name the genre they act on (`Link Sci-Fi to Science Fiction`, `Add Sci-Fi as a new genre`). Status, alerts, icons and focus as in People.

## Tests to add
- `NameLinks.test.tsx` (genres): a resolved genre shows "Normalized: Science Fiction"; an unresolved one shows its suggestion and reason; no genres shows "No genres yet."
- `NameLinks.test.tsx` (genres): "Link Sciencefiction to Science Fiction" calls `updateEntry('genres', id, { name: 'Science Fiction', aliases: [...existing, 'Sciencefiction'] })` and reloads through `getBookGenres`; "Add Horror as a new genre" calls `createEntry('genres', { name: 'Horror', aliases: [] })`.
- `NameLinks.test.tsx` (genres): a 404 shows "That genre no longer exists." on the row.
- `BookPage.test.tsx`: the headings read Chosen match, Cover, Metadata, People, Genres, Catalog search; a metadata save calls `getBookGenres` again.

## Manual checks
- On `/books/:id` with genres `Sciencefiction` and `Horror` saved and a `Science Fiction` genre on `/genres`: Sciencefiction suggests Science Fiction; link it and the row shows "Normalized: Science Fiction", and `/genres` lists the alias. Add Horror as a new genre: it appears on `/genres`.
- Tab through the People and Genres sections: each button names what it acts on; focus lands on the section heading after a link.

## Out of scope
- Linking to a genre that isn't suggested (use `/genres`)
- Book-to-token mapping and the preview (next phase 7 slices)

## Notes
- File count: 6 (`types/names.ts`, `services/api.ts`, `NameLinks.tsx` (renamed), `NameLinks.test.tsx` (renamed), `BookPage.tsx`, `BookPage.test.tsx`). Under the cap.
- Why next: slice 49's follow-up; with it, phase 7's linking done-when line (authors, narrators and genres link to canonical entries and show as normalized) is met.
- The rename is part of the task, not a drive-by refactor: one component now serves both kinds, like `NamesPage` (slice 42).
- Test runs keep full output so a repeat of the accepted slice 49 flake names its test (CURRENT-STATE decision).
- Conflict check: same rules as slice 47 (suggest only; links are aliases; no confirmation, undo on `/genres`). No new packages.

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 324 passed, 0 failed | Full output kept (TRX + console) per the slice 49 decision |
| Frontend tests | 239 passed, 0 failed | Was 232; `NameLinks` + `BookPage` files: 37 passed |
| New tests | 4 of 4 present, passing | `NameLinks.test.tsx`: 6 genre tests (resolved/unresolved without `h3`, "No genres yet.", link via `updateEntry('genres', …)` with reload and focus, add via `createEntry('genres', …)`, 404 message, load failure); the 9 people tests pass with `kind="people"`. `BookPage.test.tsx`: heading order includes Genres; a metadata save reloads Genres |
| Manual: link and add genres on `/books/:id`, check `/genres` | Not run | API and client not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: tab through People and Genres | Not run | Same |

Data checks: none (client only).

## Outcome (2026-10-19, commit 5efd415)

**Status:** Implemented
**Summary:** `PeopleLinks` became `NameLinks` with a `kind` prop and a per-kind table (heading, loader, texts, noun). The book page shows People then Genres; genres resolve through `GET /api/groups/{id}/genres` and link or add through the genre endpoints. Both sections share one reload key bumped by a pick or a metadata save.
**Changed:** 6 files: `NameLinks.tsx` and `NameLinks.test.tsx` (renamed, +6 tests), `types/names.ts` (`BookGenres`), `services/api.ts` (`getBookGenres`), `BookPage.tsx` (`namesReloadKey`, two sections), `BookPage.test.tsx` (mock, heading order, +1 test).
**Deviations:**
- Loading and load-failure texts for genres ("Loading the genres…", "Genre links couldn't be loaded.") weren't specified; they mirror People: *fine*.
- No direct `getBookGenres` test in `api.test.ts`, matching `getBookPeople` (pass-through, mocked in component tests): *fine*.
**Follow-ups:**
- Phase 7's linking done-when line is now met by 46–50. Next: book-to-token mapping (canonical names into template tokens), then the preview endpoint and screen.
- Manual checks not run (servers down); carried to the phase 7 verification.
