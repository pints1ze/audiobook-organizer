# Slice 89: Accept suggested API

**Phase:** 11, Parallel identification
**Goal:** the review list says which books have a suggested match, and one request accepts the best suggestion for each of several selected books.

## Done when
- [x] `GET /api/review` gives each book a `suggestedScore`: the score of its best stored candidate (rank 0), or null when it has none
- [x] `POST /api/review/accept-suggested` with `{ groupIds: [...] }` picks each listed book's best stored candidate through `AudiobookService.PickMatch`, exactly as `PUT /api/groups/{id}/match` would, and returns one result per id in request order: `{ groupId, accepted, error }`
- [x] a book is skipped with an error, never changed, when it is unknown ("This book no longer exists."), already has a chosen match ("This book already has a match."), or has no stored candidate ("No suggested match."); the others are still accepted
- [x] an empty or missing `groupIds`, or more than 500 ids, is a 400 with a `groupIds` error; duplicate ids are handled once
- [x] all four checks pass (build, backend tests, typecheck, frontend tests)

## Read first
- `docs/PRD.md` item 12 (a match is never applied without a click; selected books accept their suggested match in one action)
- `src/AudiobookOrganizer.Api/Services/CandidateService.cs` and `Data/CandidateRepository.cs` (slices 86, 88)
- `src/AudiobookOrganizer.Api/Services/AudiobookService.cs` (`PickMatch`, `GetMatch`)
- `src/AudiobookOrganizer.Api/Services/ReviewService.cs`, `Controllers/ReviewController.cs` and `Models/Api/ReviewDtos.cs`
- `src/AudiobookOrganizer.Api.Tests/Controllers/ReviewControllerTests.cs` and `GroupCandidatesControllerTests.cs` (seeding, fake providers)

## Tasks
- [x] **Best score in the list.** In `Models/Api/ReviewDtos.cs` add `int? SuggestedScore` as the last `ReviewBookDto` parameter. In `ReviewService.List` fill it from the book's rank-0 stored candidate (add `CandidateRepository.GetBestScores()` returning a `groupId → score` dictionary in one query, so the list doesn't read per book). Verify with a `ReviewControllerTests` test.
- [x] **Accept.** In `CandidateService` add `AcceptSuggested(IReadOnlyList<long> groupIds)` returning `IReadOnlyList<AcceptSuggestedResultDto>` (`GroupId`, `Accepted`, `Error`), checking per id in order (distinct): group exists, no current match, a stored rank-0 candidate; then `audiobooks.PickMatch(groupId, candidate)`. A `PickMatch` validation error becomes that book's error (first message). Add the DTOs (`AcceptSuggestedDto(IReadOnlyList<long>? GroupIds)`, `AcceptSuggestedResultDto`, and `AcceptSuggestedResponseDto(IReadOnlyList<AcceptSuggestedResultDto> Results)`) to `ReviewDtos.cs`.
- [x] **Endpoint.** In `ReviewController.cs` add `POST accept-suggested`: validates `groupIds` (1–500 ids) with a `ValidationProblem` like `MetadataController`, then returns 200 with the results. Inject `CandidateService` (transient).
- [x] **Tests** (below), then all four checks.
- Data: no schema change; reads `MetadataCandidate` and writes matches only through `PickMatch`.
- Auth: none (no auth in the app).
- Accessibility: none (no UI in this slice).

## Tests to add
- `Api.Tests/Controllers/ReviewControllerTests.cs`: a book with stored candidates lists their best score as `suggestedScore`; an unsearched book lists null.
- `Api.Tests/Controllers/ReviewAcceptSuggestedControllerTests.cs`: two searched books are both accepted; afterwards each `GET /api/groups/{id}/match` returns its rank-0 candidate and `GET /api/review` no longer lists them as NeedsMatch.
- Same file: a mixed request (one accepted, one already matched, one never searched, one unknown id) returns four results in order with the right errors, and the already-matched book's match is unchanged.
- Same file: missing, empty and 501-id `groupIds` are 400s with a `groupIds` error.

## Manual checks
- With the dev API restarted and books searched in the background, `POST /api/review/accept-suggested` with two group ids from `GET /api/review` accepts both; the Review list shows their new stage after a reload.

## Out of scope
- Selection checkboxes and the "Accept suggested match" bar in the Review list (next slice; it also adds `suggestedScore` to the client's `ReviewBook` type).
- A score threshold: the user chose the books, so the best suggestion is accepted whatever its score.
- Batch approve.

## Notes
- Conflict check: accepting happens only on the user's request with ids the user selected, so "never applied without a click" holds. Books with a match are skipped rather than overwritten, so a batch never replaces a match the user picked.
- `PickMatch` marks each accepted group `UserCorrected`, as a single pick does.
- The 500-id cap is a trust-boundary limit, not a feature.
- Files touched: `ReviewDtos.cs`, `ReviewService.cs`, `ReviewController.cs`, `CandidateService.cs`, `CandidateRepository.cs`, `ReviewControllerTests.cs`, `ReviewAcceptSuggestedControllerTests.cs` (new): 7.
- Later slices for this phase: the Review list's selection and "Accept suggested match" bar; select Ready books and approve them in one action.
