namespace AudiobookOrganizer.Api.Configuration;

public class ToolSettings
{
    public const string SectionName = "Tools";

    /// <summary>ffmpeg executable: a bare name resolved on PATH, or a full path.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>ffprobe executable: a bare name resolved on PATH, or a full path.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>How long one ffmpeg or ffprobe run may take before it's killed.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
