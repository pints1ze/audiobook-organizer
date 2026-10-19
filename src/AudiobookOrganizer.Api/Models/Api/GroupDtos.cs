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

/// <summary>A file the user excluded. It stays out of every group until the user sends it back to unresolved.</summary>
public record ExcludedFileDto(long FileId, string RelativePath, long? DurationMs);

public record GroupListDto(
    IReadOnlyList<GroupSummaryDto> Groups,
    IReadOnlyList<UnresolvedFileDto> Unresolved,
    IReadOnlyList<ExcludedFileDto> Excluded);

/// <summary>A file in a group. Position is its 1-based place in the book; Disc and Track are as read from its tags.</summary>
public record GroupFileDto(long FileId, string RelativePath, int Position, int? Disc, int? Track, long? DurationMs);

/// <summary>A group's summary fields plus its files in position order.</summary>
public record GroupDetailDto(
    long Id,
    string? Title,
    string? Author,
    string Confidence,
    IReadOnlyList<string> Evidence,
    int FileCount,
    long TotalDurationMs,
    bool UserCorrected,
    IReadOnlyList<GroupFileDto> Files);

/// <summary>The body of PUT /api/groups/{id}/order: every file id in the group, in the new order.</summary>
public record ReorderGroupDto(IReadOnlyList<long>? FileIds);

/// <summary>The body of POST /api/groups/{id}/files: files to append to the group, in order, taken from their old groups.</summary>
public record MoveFilesDto(IReadOnlyList<long>? FileIds);

/// <summary>The body of POST /api/groups: a new user-made group from these files, in order. Text is trimmed; blank becomes null.</summary>
public record CreateGroupDto(IReadOnlyList<long>? FileIds, string? Title, string? Author);
