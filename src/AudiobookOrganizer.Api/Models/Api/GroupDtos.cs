namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A proposed group. Confidence is "Low", "Medium" or "High".</summary>
public record GroupSummaryDto(
    long Id,
    string? Title,
    string? Author,
    string Confidence,
    IReadOnlyList<string> Evidence,
    int FileCount,
    long TotalDurationMs);

/// <summary>A file the engine couldn't place in a group. RelativePath is relative to incoming, with '/' separators.</summary>
public record UnresolvedFileDto(long FileId, string RelativePath, long? DurationMs);

public record GroupListDto(IReadOnlyList<GroupSummaryDto> Groups, IReadOnlyList<UnresolvedFileDto> Unresolved);
