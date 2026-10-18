using System.Text.Json;

namespace AudiobookOrganizer.Api.Services;

public record Chapter(string Title, TimeSpan Start);

/// <summary>
/// Reads chapter markers from MP3 (ID3v2 CHAP frames, via TagLibSharp) and M4B/MP4 (via ffprobe,
/// which reads both the Nero <c>chpl</c> atom and a QuickTime chapter track).
/// Returns an empty list when a file has no chapters; throws when the file itself can't be read.
/// </summary>
public class ChapterReader(FfmpegRunner ffmpeg)
{
    public IReadOnlyList<Chapter> Read(string path)
    {
        // Opening with TagLib validates the file (throws on missing, unsupported or corrupt files).
        using var file = TagLib.File.Create(path);

        return file switch
        {
            TagLib.Mpeg4.File => ReadMp4(path),
            _ => ReadId3v2(file),
        };
    }

    private static List<Chapter> ReadId3v2(TagLib.File file)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2) is not TagLib.Id3v2.Tag tag) return [];

        return tag.GetFrames<TagLib.Id3v2.ChapterFrame>()
            .Select(chap => new Chapter(
                chap.SubFrames.OfType<TagLib.Id3v2.TextInformationFrame>()
                    .FirstOrDefault(f => f.FrameId == "TIT2")?.Text.FirstOrDefault() ?? "",
                TimeSpan.FromMilliseconds(chap.StartMilliseconds)))
            .OrderBy(c => c.Start)
            .ToList();
    }

    private List<Chapter> ReadMp4(string path)
    {
        var json = ffmpeg.Ffprobe(["-v", "error", "-show_chapters", "-of", "json", path]);
        var output = JsonSerializer.Deserialize<FfprobeOutput>(json, JsonOptions);

        return (output?.Chapters ?? [])
            .Select(c => new Chapter(c.Tags?.GetValueOrDefault("title") ?? "", ToTimeSpan(c.Start, c.TimeBase)))
            .OrderBy(c => c.Start)
            .ToList();
    }

    /// <summary>Converts a start in time-base units (e.g. "1/22050") to a TimeSpan without float rounding.</summary>
    private static TimeSpan ToTimeSpan(long start, string timeBase)
    {
        var parts = timeBase.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var num) || !long.TryParse(parts[1], out var den) || den == 0)
            throw new FormatException($"Unexpected ffprobe time_base '{timeBase}'.");

        return TimeSpan.FromTicks((long)((Int128)start * num * TimeSpan.TicksPerSecond / den));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private record FfprobeOutput(List<FfprobeChapter>? Chapters);

    private record FfprobeChapter(long Start, string TimeBase, Dictionary<string, string>? Tags);
}
