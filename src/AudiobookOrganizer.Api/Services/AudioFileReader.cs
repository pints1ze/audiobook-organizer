using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudiobookOrganizer.Api.Services;

public record AudioFileMetadata(
    string? Title,
    string? Album,
    string? Artist,
    string? AlbumArtist,
    string? Composer,
    string? Genre,
    int? Year,
    int? Track,
    int? Disc,
    string? Comment,
    long DurationMs,
    bool HasCover,
    int ChapterCount);

/// <summary>
/// Reads tags, duration, cover presence and chapter count from an MP3 or M4B. Read-only: the file is
/// opened with TagLibSharp and never saved. When TagLibSharp can't parse a file (e.g. a broken ID3v2 chapter table),
/// the tags, duration and chapter count come from one ffprobe call instead. Exceptions propagate so the caller can
/// record them.
/// </summary>
public class AudioFileReader(ChapterReader chapters, FfmpegRunner ffmpeg, ILogger<AudioFileReader> logger)
{
    // Virtual so tests can substitute a gated fake that holds a scan open.
    public virtual AudioFileMetadata Read(string path)
    {
        try
        {
            return ReadWithTagLib(path);
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or FfmpegException))
        {
            // When ffprobe can't read it either, the TagLibSharp error is the one worth recording.
            if (TryReadWithFfprobe(path) is not { } metadata) throw;
            logger.LogWarning("TagLibSharp couldn't read {Path} ({Error}); read it through ffprobe instead", path, ex.Message);
            return metadata;
        }
    }

    private AudioFileMetadata ReadWithTagLib(string path)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;

        return new AudioFileMetadata(
            Text(tag.Title),
            Text(tag.Album),
            Join(tag.Performers),
            Join(tag.AlbumArtists),
            Join(tag.Composers),
            Join(tag.Genres),
            Number(tag.Year),
            Number(tag.Track),
            Number(tag.Disc),
            Text(tag.Comment),
            ReadDurationMs(path),
            tag.Pictures.Length > 0,
            chapters.Read(path).Count);
    }

    private AudioFileMetadata? TryReadWithFfprobe(string path)
    {
        try
        {
            return ReadWithFfprobe(path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ffprobe couldn't read {Path} either", path);
            return null;
        }
    }

    /// <summary>Tags, duration and chapter count from ffprobe; no cover (covers are read through TagLibSharp).</summary>
    private AudioFileMetadata ReadWithFfprobe(string path)
    {
        var json = ffmpeg.Ffprobe(["-v", "error", "-show_format", "-show_chapters", "-of", "json", path]);
        var output = JsonSerializer.Deserialize<FfprobeOutput>(json, JsonOptions)
            ?? throw new FormatException("ffprobe returned no output.");
        if (!double.TryParse(output.Format?.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            throw new FormatException($"Unexpected ffprobe duration '{output.Format?.Duration}'.");

        var tags = output.Format?.Tags ?? [];
        string? Tag(string key) => Text(tags.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)).Value);

        return new AudioFileMetadata(
            Tag("title"),
            Tag("album"),
            Tag("artist"),
            Tag("album_artist"),
            Tag("composer"),
            Tag("genre"),
            Tag("date") is { } date && Regex.Match(date, @"\d{4}") is { Success: true } year ? int.Parse(year.Value, CultureInfo.InvariantCulture) : null,
            LeadingNumber(Tag("track")),
            LeadingNumber(Tag("disc")),
            Tag("comment"),
            (long)Math.Round(seconds * 1000),
            false,
            output.Chapters?.Count ?? 0);
    }

    /// <summary>The number before any "/" ("3/12" is 3); null when it's missing, not a number or 0.</summary>
    private static int? LeadingNumber(string? value) =>
        int.TryParse(value?.Split('/')[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;

    // TagLibSharp under-reports MP3 duration for low-sample-rate (MPEG-2) files such as the fixture
    // (17.2 s for 30 s), so duration comes from ffprobe's container duration for every format.
    private long ReadDurationMs(string path)
    {
        var output = ffmpeg.Ffprobe(["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path]).Trim();
        if (!double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            throw new FormatException($"Unexpected ffprobe duration '{output}'.");
        return (long)Math.Round(seconds * 1000);
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Join(string[] values) => Text(string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v))));

    private static int? Number(uint value) => value == 0 ? null : (int)value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private record FfprobeOutput(FfprobeFormat? Format, List<JsonElement>? Chapters);

    private record FfprobeFormat(string? Duration, Dictionary<string, string>? Tags);
}
