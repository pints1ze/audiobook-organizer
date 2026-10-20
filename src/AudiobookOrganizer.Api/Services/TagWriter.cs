namespace AudiobookOrganizer.Api.Services;

/// <summary>A cover image to embed: JPEG or PNG bytes and their MIME type.</summary>
public record TagCover(byte[] Image, string MimeType);

/// <summary>
/// The standard tags written into each copy of a book's file. Title is also the album, and Artists also the album
/// artists. A null or empty value leaves the source's tag as it was; a null cover leaves its pictures as they were.
/// </summary>
public record BookTags(
    string? Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Genres,
    int? Year,
    int Track,
    int TrackCount,
    int? Disc,
    TagCover? Cover);

/// <summary>Thrown before anything is created when the target path already exists, so it was never the writer's.</summary>
public class TagTargetExistsException(string path, Exception? inner = null) : IOException($"A file is already at {path}.", inner);

/// <summary>
/// Writes a tagged copy of a source file to a new path. The source is only read. MP3 copies are tagged with
/// TagLibSharp; M4B copies are first remuxed by ffmpeg (<c>-c copy</c>) without the Nero <c>chpl</c> chapter atom,
/// which TagLibSharp corrupts, and then tagged the same way.
/// </summary>
public class TagWriter(FfmpegRunner ffmpeg)
{
    /// <summary>
    /// Creates <paramref name="tempPath"/> as a tagged copy of <paramref name="sourcePath"/>. Throws
    /// <see cref="TagTargetExistsException"/> when it already exists (nothing is created then); after any other
    /// failure, a file at <paramref name="tempPath"/> is the caller's to remove.
    /// </summary>
    public void Write(string sourcePath, string tempPath, BookTags tags)
    {
        if (File.Exists(tempPath) || Directory.Exists(tempPath)) throw new TagTargetExistsException(tempPath);
        if (string.Equals(Path.GetExtension(sourcePath), ".m4b", StringComparison.OrdinalIgnoreCase))
            WriteM4b(sourcePath, tempPath, tags);
        else
            WriteMp3(sourcePath, tempPath, tags);
    }

    private static void WriteMp3(string sourcePath, string tempPath, BookTags tags)
    {
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var output = CreateNew(tempPath);
            source.CopyTo(output);
        }
        if (new FileInfo(tempPath).Length != new FileInfo(sourcePath).Length)
            throw new IOException($"The copy of {sourcePath} isn't the same size as the source.");

        // The .partial name has no audio extension, so the type is named.
        using var file = TagLib.File.Create(tempPath, "audio/mpeg", TagLib.ReadStyle.Average);
        ApplyTags(file, tags, joinLists: false);
        file.Save();
    }

    private void WriteM4b(string sourcePath, string tempPath, BookTags tags)
    {
        // An empty placeholder made with CreateNew proves the path is this writer's; ffmpeg then replaces only it
        // (-y). ffmpeg's own -n refusal exits 0, so it couldn't tell a race from success.
        CreateNew(tempPath).Dispose();

        // Remux without the Nero chpl atom: chapters stay in the QuickTime chapter track, and TagLibSharp (which
        // corrupts chpl when it saves) then has nothing to damage. -map_metadata 0 keeps the source's tags and cover.
        // ipod is the muxer ffmpeg picks for .m4b; it's named because the .partial name has no extension.
        ffmpeg.Ffmpeg(
        [
            "-y", "-v", "error", "-i", sourcePath, "-map", "0:a", "-map", "0:v?", "-c", "copy", "-map_chapters", "0",
            "-map_metadata", "0", "-disposition:v:0", "attached_pic", "-movflags", "disable_chpl", "-f", "ipod", tempPath,
        ]);

        using var file = TagLib.File.Create(tempPath, "audio/mp4", TagLib.ReadStyle.Average);
        ApplyTags(file, tags, joinLists: true);
        file.Save();
    }

    /// <summary>
    /// Writes the book's tags. A null or empty value leaves the tag alone; a chosen cover replaces the pictures.
    /// <paramref name="joinLists"/> joins artists and genres with ", " into one value (M4B).
    /// </summary>
    private static void ApplyTags(TagLib.File file, BookTags tags, bool joinLists)
    {
        string[] List(IReadOnlyList<string> values) => joinLists ? [string.Join(", ", values)] : [.. values];

        var tag = file.Tag;
        if (Text(tags.Title) is { } title)
        {
            tag.Title = title;
            tag.Album = title;
        }
        if (tags.Artists.Count > 0)
        {
            tag.Performers = List(tags.Artists);
            tag.AlbumArtists = List(tags.Artists);
        }
        if (tags.Genres.Count > 0) tag.Genres = List(tags.Genres);
        if (tags.Year is { } year) tag.Year = (uint)year;
        tag.Track = (uint)tags.Track;
        tag.TrackCount = (uint)tags.TrackCount;
        if (tags.Disc is { } disc) tag.Disc = (uint)disc;
        if (tags.Cover is { } cover)
            tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(cover.Image)) { Type = TagLib.PictureType.FrontCover, MimeType = cover.MimeType },
            ];
    }

    /// <summary>Opens a new file for writing; throws <see cref="TagTargetExistsException"/> if one is already there.</summary>
    private static FileStream CreateNew(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) throw new TagTargetExistsException(path);
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException ex) when (File.Exists(path))
        {
            throw new TagTargetExistsException(path, ex);
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
