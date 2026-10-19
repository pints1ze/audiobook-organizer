namespace AudiobookOrganizer.Api.Services.Grouping;

/// <summary>One scanned file as the grouping engine sees it. RelativePath is relative to incoming, with '/' separators.</summary>
public record GroupingInput(
    long FileId,
    string RelativePath,
    string Extension,
    string? Title = null,
    string? Album = null,
    string? Artist = null,
    string? AlbumArtist = null,
    int? Track = null,
    int? Disc = null,
    long? DurationMs = null);

public enum GroupConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>A file inside a proposed group, with the disc and track the engine resolved from tags, folders or the filename.</summary>
public record GroupedFile(long FileId, int? Disc, int? Track);

public record ProposedGroup(
    string? EstimatedTitle,
    string? EstimatedAuthor,
    GroupConfidence Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<GroupedFile> Files,
    long TotalDurationMs);

public record GroupingResult(IReadOnlyList<ProposedGroup> Groups, IReadOnlyList<long> Unresolved);
