namespace AudiobookOrganizer.Api.Models.Local;

/// <summary>A proposed book: files the grouping engine put together after a scan. Replaced on every completed scan.</summary>
public class FileGroup
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string Confidence { get; set; } = "";
    public string EvidenceJson { get; set; } = "[]";
    public int FileCount { get; set; }
    public long TotalDurationMs { get; set; }
    public long ScanRunId { get; set; }
}
