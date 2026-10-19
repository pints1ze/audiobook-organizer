namespace AudiobookOrganizer.Api.Models.Local;

/// <summary>An MP3 or M4B found in incoming. Rows keep their Id across rescans and are never removed by a scan.</summary>
public class AudioFile
{
    public long Id { get; set; }
    public string SourcePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public string ModifiedAtUtc { get; set; } = "";
    public long? DurationMs { get; set; }
    public string? Title { get; set; }
    public string? Album { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Composer { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public int? Track { get; set; }
    public int? Disc { get; set; }
    public string? Comment { get; set; }
    public bool HasCover { get; set; }
    public int? ChapterCount { get; set; }
    public string? ScanError { get; set; }
    public long FirstSeenScanId { get; set; }
    public long LastSeenScanId { get; set; }
}
