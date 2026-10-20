# Slice 47: Book people links UI

**Phase:** 7, Naming & preview
**Goal:** the book page shows each author and narrator as linked to a canonical person or not, and lets the user link an unresolved name to a suggested person or add it as a new person.

## Done when
- [x] the book page has a "People" section listing Authors and Narrators from `GET /api/groups/{id}/people`, one row per name in order
- [x] a resolved name shows "Normalized" and the canonical name (e.g. `Author, Ann` → "Normalized: Ann Author"); an unresolved one shows "Not linked"
- [x] each suggestion on an unresolved name shows the person and the reason, with a "Link to {person}" button that adds the name as that person's alias (`PUT /api/people/{id}` with the entry's name and its aliases plus the name); "Add as new person" creates it (`POST /api/people` with `{ name, aliases: [] }`). After either, the section reloads and the row shows as normalized
- [x] a failed link or add (400 message, 404, network) shows an inline error on that row and leaves the row as it was; the section reloads after a match is picked or the metadata editor saves
- [x] all four checks pass

## Read first
- `docs/slices/46-book-people-links-api.md` (response shape, Outcome)
- `src/AudiobookOrganizer.Api/Models/Api/CanonicalEntryDtos.cs` (`NameSuggestionDto`, `NameLinkDto`, `BookPeopleDto`)
- `src/AudiobookOrganizer.Client/src/pages/BookPage.tsx` (`BookContent`, `metadataReloadKey`) and `components/book/MetadataEditor.tsx` (load/save pattern, `mounted` ref, error and focus handling)
- `src/AudiobookOrganizer.Client/src/services/api.ts` (`updateEntry`, `createEntry`, `toSaveEntryResult`) and `types/names.ts`
- `src/AudiobookOrganizer.Client/src/components/names/MergeSuggestions.tsx` (how reasons and confirmations are shown on the People page)

## Tasks
- [x] **Types and API call.** `types/names.ts`: `NameSuggestion { entry: CanonicalEntry; reason: string }`, `NameLink { name: string; entry: CanonicalEntry | null; suggestions: NameSuggestion[] }`, `BookPeople { groupId: number; authors: NameLink[]; narrators: NameLink[] }`. `services/api.ts`: `getBookPeople(groupId): Promise<BookPeople>`, throwing on any non-200 like `getEntries`.
- [x] **`PeopleLinks` component.** New `components/book/PeopleLinks.tsx` with props `{ groupId, reloadKey }`. Loads on mount and whenever `reloadKey` changes (same `current` flag pattern as `MetadataEditor`). Loading, failed ("People links couldn't be loaded.") and empty ("No authors or narrators yet.") states. Two lists (Authors, Narrators), each row: the name; "Normalized: {entry.name}" when resolved, otherwise "Not linked", its suggestions ("{entry.name} ({reason})" with a "Link to {entry.name}" button) and an "Add as new person" button. Link: `updateEntry('people', entry.id, { name: entry.name, aliases: [...entry.aliases, name] })`. Add: `createEntry('people', { name, aliases: [] })`. While one action runs, all row buttons are disabled. On success reload the section and put "{name} linked to {person}." in a `role="status"` line. On failure show a `role="alert"` error on that row: the first 400 field message, "That person no longer exists." for 404, or a connection message when it throws.
- [x] **Book page wiring.** `components/book/MetadataEditor.tsx`: optional `onSaved?: () => void`, called after a successful save. `pages/BookPage.tsx`: render `<PeopleLinks>` after `MetadataEditor`, with a `peopleReloadKey` that bumps when a match is picked (alongside `metadataReloadKey`) and on `onSaved`.
- [x] **Tests.** New `components/book/PeopleLinks.test.tsx` (below); `pages/BookPage.test.tsx` mocks `getBookPeople` (and `updateEntry`/`createEntry` if needed) so existing tests keep passing, plus one test below. Run all four checks.
- Data: none (client only; links are aliases, stored by the existing people endpoints).
- Auth: none (single user, no login).
- Accessibility: the section has an `h2` "People" linked by `aria-labelledby`, with `h3` Authors and Narrators. Each button's accessible name includes the book name it acts on (e.g. `aria-label="Link Nia Narrator to N. Narrator"`, `"Add Nia Narrator as a new person"`). Status uses `role="status"`; errors use `role="alert"` with a `CircleAlert` icon (`aria-hidden`) beside the text, never colour alone. After a successful action, focus moves to the section heading (`tabIndex={-1}`) because the clicked button disappears on reload; after a failure, focus stays on the button.

## Tests to add
- `PeopleLinks.test.tsx`: renders a resolved author ("Normalized: Ann Author") and an unresolved narrator with one suggestion and its reason; empty and failed loads show their messages.
- `PeopleLinks.test.tsx`: "Link Nia Narrator to N. Narrator" calls `updateEntry('people', id, { name: 'N. Narrator', aliases: [...existing, 'Nia Narrator'] })`, reloads, shows the status line and moves focus to the heading.
- `PeopleLinks.test.tsx`: "Add Nia Narrator as a new person" calls `createEntry('people', { name: 'Nia Narrator', aliases: [] })`; a 400 result (`{ ok: false, errors: { aliases: ['…'] } }`) shows that message as an alert on the row and doesn't reload.
- `BookPage.test.tsx`: picking a match reloads the people section (`getBookPeople` called a second time).

## Manual checks
- With the API and client running, open a book with a picked match on `/books/:id`: names known on `/people` show "Normalized"; others show suggestions or none.
- Click "Link to…" on a suggestion: the row becomes normalized and `/people` shows the name as an alias of that person. Click "Add as new person" on another: it appears on `/people`.
- Edit the narrators in the Metadata section and save: the People section updates. Tab through the section: every button names the book name it acts on, and focus lands on the "People" heading after a link.

## Out of scope
- Linking to a person who isn't suggested (a pick-any-person combobox): use `/people` to add the alias for now; a later slice if it's missed
- Showing "Normalized" origins inside the Metadata section (decision from slice 46: the people response is where a value shows as normalized)
- Genres on books and their links (next phase 7 slice); book-to-token mapping and the preview
- A merge test in `GroupPeopleControllerTests.cs` (slice 46 debt; this slice touches no backend tests)

## Notes
- File count: 7 (`types/names.ts`, `services/api.ts`, new `components/book/PeopleLinks.tsx`, new `PeopleLinks.test.tsx`, `MetadataEditor.tsx`, `BookPage.tsx`, `BookPage.test.tsx`). Under the cap.
- Why next: slice 46's Outcome names it, and PLAN.md's phase 7 done-when needs linked values shown as normalized.
- `PUT /api/people/{id}` replaces the whole alias list, so the link sends the entry's aliases as returned by the people GET plus the new name. If someone edited that person on `/people` between the load and the click, those edits would be lost; acceptable for one user, and the reload right after shows the true state.
- A name whose key already belongs to another person returns a 400 from both endpoints; the row shows that message (slice 38 rule).
- Conflict check: keeps "fuzzy matches are only ever suggested" (slice 38): nothing links without a click, and there's no confirmation step because a link is undone by removing the alias on `/people`. No new packages or shadcn components (plain buttons and lists).

## Test results (2026-10-19)

**Regressions:** none

| Check | Result | Notes |
|---|---|---|
| Build | Passed | 0 warnings, 0 errors (`--no-incremental`) |
| Typecheck | Passed | `npx tsc -b --noEmit` exit 0 |
| Backend tests | 319 passed, 0 failed | Unchanged (client-only slice) |
| Frontend tests | 228 passed, 0 failed | Was 217; `PeopleLinks` + `BookPage` files: 30 passed |
| New tests | 4 of 4 present, passing | `PeopleLinks.test.tsx`: resolved/unresolved render, empty and failed loads, link via `updateEntry` with focus to heading, add via `createEntry`, 400 alert without reload (plus 404, throw, disabled-while-running). `BookPage.test.tsx`: reload after "Use this match" (plus reload after metadata save) |
| Manual: People section shows normalized, suggestions or none | Not run | API and client not running. Start with `scripts/start-api.ps1` and `scripts/start-client.ps1` |
| Manual: "Link to…" and "Add as new person" update the row and `/people` | Not run | Same |
| Manual: metadata save updates People; keyboard names and focus | Not run | Same |

Data checks: none (no schema change; links are aliases stored by the existing people endpoints).

## Outcome (2026-10-19, commit 7e40838)

**Status:** Implemented
**Summary:** The book page has a People section (`PeopleLinks`) listing each author and narrator from `GET /api/groups/{id}/people` as "Normalized: {person}" or "Not linked" with its suggestions. "Link to…" adds the name as that person's alias and "Add as new person" creates one, through the existing people endpoints. The section reloads after a link, a picked match or a metadata save.
**Changed:** 7 files: new `components/book/PeopleLinks.tsx` and `PeopleLinks.test.tsx` (9 tests), `types/names.ts` (3 types), `services/api.ts` (`getBookPeople`), `MetadataEditor.tsx` (optional `onSaved`), `BookPage.tsx` (section plus `peopleReloadKey`), `BookPage.test.tsx` (mocks, +2 tests, heading order).
**Deviations:**
- Two `BookPage.test.tsx` heading-order assertions now include "People" between Metadata and Catalog search: *fine*.
- An extra BookPage test covers the reload after a metadata save: *improvement*.
- An empty list shows "None" when the other list has names, and focus goes back to the clicked button after a failure (disabling it can drop focus): *improvement*.
- The slice file and PLAN.md stayed out of the `feat:` commit for this docs commit, as in slices 45–46: *fine*.
**Follow-ups:**
- Next: a hand-entered genres field on books with links (same pattern: API, then the book page), then book-to-token mapping and the preview.
- Linking to a person who isn't suggested isn't possible from the book page; use `/people` (out of scope, revisit if missed).
- Manual checks not run (servers down); carried to the phase 7 verification.
