namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// One unprocessed book in the review list. Title, Author (first author) and Narrator (first narrator) are the book's
/// metadata values, file fallback included. Stage is "NeedsMatch", "NeedsReview", "Ready", "Queued" or "Failed".
/// CoverSource is "chosen", "embedded" or null; CoverFileId is the first file with an embedded cover when it is
/// "embedded", else null. CoverChosenAtUtc is when the chosen cover was chosen (null unless "chosen"); the client
/// versions the cover image URL with it so a changed cover isn't served from the browser cache. SuggestedScore is the
/// score of the book's best stored candidate (rank 0), or null when it has none.
/// </summary>
public record ReviewBookDto(
    long GroupId, string? Title, string? Author, string? Narrator, long DurationMs, int FileCount, string Stage,
    string? CoverSource, long? CoverFileId, DateTimeOffset? CoverChosenAtUtc, int? SuggestedScore);

/// <summary>How many listed books are in each stage.</summary>
public record ReviewCountsDto(int NeedsMatch, int NeedsReview, int Ready, int Queued, int Failed);

/// <summary>Every unprocessed book in GET /api/groups order, with the counts per stage.</summary>
public record ReviewListDto(IReadOnlyList<ReviewBookDto> Books, ReviewCountsDto Counts);

/// <summary>POST /api/review/accept-suggested: the books whose best stored candidate to accept.</summary>
public record AcceptSuggestedDto(IReadOnlyList<long>? GroupIds);

/// <summary>One book's outcome: Accepted, or skipped with Error and left unchanged.</summary>
public record AcceptSuggestedResultDto(long GroupId, bool Accepted, string? Error);

/// <summary>One result per distinct requested id, in request order.</summary>
public record AcceptSuggestedResponseDto(IReadOnlyList<AcceptSuggestedResultDto> Results);
