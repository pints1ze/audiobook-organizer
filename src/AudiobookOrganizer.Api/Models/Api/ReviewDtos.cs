namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// One unprocessed book in the review list. Title, Author (first author) and Narrator (first narrator) are the book's
/// metadata values, file fallback included. Stage is "NeedsMatch", "NeedsReview", "Ready", "Queued" or "Failed".
/// CoverSource is "chosen", "embedded" or null; CoverFileId is the first file with an embedded cover when it is
/// "embedded", else null.
/// </summary>
public record ReviewBookDto(
    long GroupId, string? Title, string? Author, string? Narrator, long DurationMs, int FileCount, string Stage,
    string? CoverSource, long? CoverFileId);

/// <summary>How many listed books are in each stage.</summary>
public record ReviewCountsDto(int NeedsMatch, int NeedsReview, int Ready, int Queued, int Failed);

/// <summary>Every unprocessed book in GET /api/groups order, with the counts per stage.</summary>
public record ReviewListDto(IReadOnlyList<ReviewBookDto> Books, ReviewCountsDto Counts);
