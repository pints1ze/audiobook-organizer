namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A group. Confidence is "Low", "Medium" or "High". UserCorrected groups survive rescans unchanged.</summary>
public record GroupSummaryDto(
    long Id,
    string? Title,
    string? Author,
    string Confidence,
    IReadOnlyList<string> Evidence,
    int FileCount,
    long TotalDurationMs,
    bool UserCorrected);

/// <summary>The body of PUT /api/groups/{id}. Values are trimmed; blank becomes null.</summary>
public record UpdateGroupDto(string? Title, string? Author);

/// <summary>A file the engine couldn't place in a group. RelativePath is relative to incoming, with '/' separators.</summary>
public record UnresolvedFileDto(long FileId, string RelativePath, long? DurationMs);

public record GroupListDto(IReadOnlyList<GroupSummaryDto> Groups, IReadOnlyList<UnresolvedFileDto> Unresolved);
