namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// The saved directories, the latest scan (any status, or null) and the inventory counts. The counts are files seen
/// in the latest completed scan, and those of them with a read error.
/// </summary>
public record DashboardDto(
    string? IncomingPath,
    string? LibraryPath,
    ScanRunDto? LastScan,
    int AudioFileCount,
    int FilesWithErrors);
