namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// The saved directories, the latest scan (any status, or null) and the counts. The file counts are files seen in the
/// latest completed scan, those of them with a read error, and those the groups list shows as unresolved or excluded
/// (0 when no scan has completed). The book counts cover every stored group: in total, by confidence, and those the
/// user corrected.
/// </summary>
public record DashboardDto(
    string? IncomingPath,
    string? LibraryPath,
    ScanRunDto? LastScan,
    int AudioFileCount,
    int FilesWithErrors,
    int BookCount,
    BooksByConfidenceDto BooksByConfidence,
    int CorrectedBookCount,
    int UnresolvedFileCount,
    int ExcludedFileCount);

/// <summary>How many groups have each confidence.</summary>
public record BooksByConfidenceDto(int Low, int Medium, int High);
