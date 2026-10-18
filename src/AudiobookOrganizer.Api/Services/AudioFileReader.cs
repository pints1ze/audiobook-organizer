using System.Globalization;

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
/// opened with TagLibSharp and never saved. Exceptions propagate so the caller can record them.
/// </summary>
public class AudioFileReader(ChapterReader chapters, FfmpegRunner ffmpeg)
{
    // Virtual so tests can substitute a gated fake that holds a scan open.
    public virtual AudioFileMetadata Read(string path)
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
}
