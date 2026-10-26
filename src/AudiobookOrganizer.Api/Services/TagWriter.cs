namespace AudiobookOrganizer.Api.Services;

/// <summary>A cover image to embed: JPEG or PNG bytes and their MIME type.</summary>
public record TagCover(byte[] Image, string MimeType);

/// <summary>
/// The standard tags written into each copy of a book's file. Title is also the album, and Artists also the album
/// artists. A null or empty value leaves the source's tag as it was; a null cover leaves its pictures as they were.
/// The init properties are the book fields of the developer's Mp3tag Audible script (<c>mp3tagscipt.src</c>).
/// </summary>
public record BookTags(
    string? Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Genres,
    int? Year,
    int Track,
    int? Disc,
    TagCover? Cover)
{
    public IReadOnlyList<string> Narrators { get; init; } = [];
    public string? Subtitle { get; init; }
    public string? Series { get; init; }
    /// <summary>The series part as stored: "2", "1.5" or "2-3".</summary>
    public string? SeriesPart { get; init; }
    public string? Publisher { get; init; }
    public string? ReleaseDate { get; init; }
    public string? Language { get; init; }
    public string? Format { get; init; }
    public string? Isbn { get; init; }
    public string? Asin { get; init; }

    /// <summary>"Series, Book #Part", just "Series" without a part, or null without a series (CONTENTGROUP).</summary>
    public string? ContentGroup => Text(Series) is not { } series ? null
        : Text(SeriesPart) is { } part ? $"{series}, Book #{part}" : series;

    /// <summary>
    /// "Series Part - Title" with a series ("Series - Title" without a part), else "Title - Subtitle" with a subtitle,
    /// else the title; null without a title (ALBUMSORT).
    /// </summary>
    public string? AlbumSort
    {
        get
        {
            if (Text(Title) is not { } title) return null;
            if (Text(Series) is { } series)
                return Text(SeriesPart) is { } part ? $"{series} {part} - {title}" : $"{series} - {title}";
            return Text(Subtitle) is { } subtitle ? $"{title} - {subtitle}" : title;
        }
    }

    /// <summary>The book's audible.com page, or null without an ASIN (WWWAUDIOFILE).</summary>
    public string? AudibleUrl => Text(Asin) is { } asin ? $"https://www.audible.com/pd/{Uri.EscapeDataString(asin)}" : null;

    /// <summary>Trimmed, or null when blank.</summary>
    internal static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Thrown before anything is created when the target path already exists, so it was never the writer's.</summary>
public class TagTargetExistsException(string path, Exception? inner = null) : IOException($"A file is already at {path}.", inner);

/// <summary>
/// Writes a tagged copy of a source file to a new path. The source is only read. MP3 copies are tagged with
/// TagLibSharp; M4B copies are first remuxed by ffmpeg (<c>-c copy</c>) with no chapters at all, so no Nero
/// <c>chpl</c> atom for TagLibSharp to corrupt, and then tagged the same way. MP3 copies keep their chapters. An MP3
/// whose tags TagLibSharp can't parse (such as a broken chapter table) is remuxed by ffmpeg (<c>-c copy</c>, ID3v2.3)
/// instead of byte-copied, and that copy is tagged the same way.
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

    private void WriteMp3(string sourcePath, string tempPath, BookTags tags)
    {
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var output = CreateNew(tempPath);
            source.CopyTo(output);
        }
        if (new FileInfo(tempPath).Length != new FileInfo(sourcePath).Length)
            throw new IOException($"The copy of {sourcePath} isn't the same size as the source.");

        using var file = OpenMp3(tempPath) ?? RemuxMp3(sourcePath, tempPath);
        ApplyTags(file, tags, joinLists: false);
        ApplyMp3BookFrames((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true), tags);
        file.Save();
    }

    /// <summary>The MP3 copy opened by TagLibSharp, or null when it can't parse the tags (I/O errors still throw).</summary>
    private static TagLib.File? OpenMp3(string path)
    {
        try
        {
            // The .partial name has no audio extension, so the type is named.
            return TagLib.File.Create(path, "audio/mpeg", TagLib.ReadStyle.Average);
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or FfmpegException))
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the byte copy at <paramref name="tempPath"/> with an ffmpeg remux of the source (<c>-c copy</c>: the
    /// audio, any attached picture, the source's tags and chapters, rewritten as ID3v2.3), so TagLibSharp gets clean
    /// tags to open. A remux it still can't open throws.
    /// </summary>
    private TagLib.File RemuxMp3(string sourcePath, string tempPath)
    {
        File.Delete(tempPath);
        // As in WriteM4b: the CreateNew placeholder proves the path is this writer's before ffmpeg replaces it (-y).
        CreateNew(tempPath).Dispose();
        ffmpeg.Ffmpeg(
        [
            "-y", "-v", "error", "-i", sourcePath, "-map", "0:a", "-map", "0:v?", "-c", "copy", "-map_metadata", "0",
            "-id3v2_version", "3", "-f", "mp3", tempPath,
        ]);
        return TagLib.File.Create(tempPath, "audio/mpeg", TagLib.ReadStyle.Average);
    }

    private void WriteM4b(string sourcePath, string tempPath, BookTags tags)
    {
        // An empty placeholder made with CreateNew proves the path is this writer's; ffmpeg then replaces only it
        // (-y). ffmpeg's own -n refusal exits 0, so it couldn't tell a race from success.
        CreateNew(tempPath).Dispose();

        // Remux with no chapters (decision 2026-10-21: a chapter track could come back truncated and fail the
        // read-back). disable_chpl stays as a guard, so TagLibSharp (which corrupts chpl when it saves) never meets
        // one. -map_metadata 0 keeps the source's tags and cover.
        // ipod is the muxer ffmpeg picks for .m4b; it's named because the .partial name has no extension.
        ffmpeg.Ffmpeg(
        [
            "-y", "-v", "error", "-i", sourcePath, "-map", "0:a", "-map", "0:v?", "-c", "copy", "-map_chapters", "-1",
            "-map_metadata", "0", "-disposition:v:0", "attached_pic", "-movflags", "disable_chpl", "-f", "ipod", tempPath,
        ]);

        using var file = TagLib.File.Create(tempPath, "audio/mp4", TagLib.ReadStyle.Average);
        ApplyTags(file, tags, joinLists: true);
        ApplyM4bBookAtoms((TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, true), tags);
        file.Save();
    }

    /// <summary>
    /// The script's book fields as ID3v2 frames (names as Mp3tag maps them). A field the book lacks is left as the
    /// source had it.
    /// </summary>
    private static void ApplyMp3BookFrames(TagLib.Id3v2.Tag tag, BookTags tags)
    {
        void SetText(string id, string? value)
        {
            if (Text(value) is { } text) TagLib.Id3v2.TextInformationFrame.Get(tag, id, true).Text = [text];
        }
        void SetUser(string name, string? value)
        {
            if (Text(value) is { } text) TagLib.Id3v2.UserTextInformationFrame.Get(tag, name, true).Text = [text];
        }

        var narrators = tags.Narrators.Select(Text).OfType<string>().ToArray();
        if (narrators.Length > 0) tag.Composers = narrators;
        if (Text(tags.Subtitle) is { } subtitle) tag.Subtitle = subtitle;
        SetUser("SERIES", tags.Series);
        SetUser("SERIES-PART", tags.SeriesPart);
        SetText("MVNM", tags.Series);
        SetText("MVIN", tags.SeriesPart);
        if (Text(tags.Series) is not null) SetUser("SHOWMOVEMENT", "1");
        if (tags.ContentGroup is { } group) tag.Grouping = group;
        if (tags.AlbumSort is { } sort) tag.AlbumSort = sort;
        SetText("TLAN", tags.Language);
        SetText("TPUB", tags.Publisher);
        SetText("TRCK", tags.Track.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetUser("FORMAT", tags.Format);
        SetUser("ISBN", tags.Isbn);
        SetUser("ASIN", tags.Asin);
        SetText("TDRL", tags.ReleaseDate);
        if (tags.AudibleUrl is { } url) TagLib.Id3v2.UrlLinkFrame.Get(tag, "WOAF", true).Text = [url];
    }

    private const string ITunes = "com.apple.iTunes";

    // Integer data boxes carry the "signed integer" well-known type (21), which TagLibSharp names ForTempo.
    private const uint IntegerFlags = (uint)TagLib.Mpeg4.AppleDataBox.FlagType.ForTempo;

    private static readonly TagLib.ByteVector MovementName = new([0xA9, (byte)'m', (byte)'v', (byte)'n']);
    private static readonly TagLib.ByteVector MovementNumber = new([0xA9, (byte)'m', (byte)'v', (byte)'i']);

    /// <summary>
    /// The script's book fields as MP4 atoms: standard ones through the Tag API, the rest as freeform
    /// <c>----:com.apple.iTunes:</c> atoms, and the iTunes integer atoms (as proven in slice 60). A field the book
    /// lacks is left as the source had it; the media type (Audiobook) and gapless flag are always written.
    /// </summary>
    private static void ApplyM4bBookAtoms(TagLib.Mpeg4.AppleTag tag, BookTags tags)
    {
        void SetDash(string name, string? value)
        {
            if (Text(value) is { } text) tag.SetDashBox(ITunes, name, text);
        }

        var narrators = tags.Narrators.Select(Text).OfType<string>().ToArray();
        if (narrators.Length > 0) tag.Composers = [string.Join(", ", narrators)];
        SetDash("SUBTITLE", tags.Subtitle);
        SetDash("SERIES", tags.Series);
        SetDash("SERIES-PART", tags.SeriesPart);
        if (Text(tags.Series) is { } series)
        {
            tag.SetText(MovementName, series);
            tag.SetData("shwm", new TagLib.ByteVector([1]), IntegerFlags);
        }
        // ©mvi is a 16-bit integer, so a part like "1.5" or "2-3" stays only in SERIES-PART.
        if (ushort.TryParse(Text(tags.SeriesPart), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var movement))
            tag.SetData(MovementNumber, new TagLib.ByteVector([(byte)(movement >> 8), (byte)movement]), IntegerFlags);
        if (tags.ContentGroup is { } group) tag.Grouping = group;
        if (tags.AlbumSort is { } sort) tag.AlbumSort = sort;
        SetDash("LANGUAGE", tags.Language);
        SetDash("PUBLISHER", tags.Publisher);
        SetDash("FORMAT", tags.Format);
        SetDash("ISBN", tags.Isbn);
        SetDash("ASIN", tags.Asin);
        SetDash("RELEASETIME", tags.ReleaseDate);
        SetDash("WWWAUDIOFILE", tags.AudibleUrl);
        tag.SetData("stik", new TagLib.ByteVector([2]), IntegerFlags); // 2 = Audiobook
        tag.SetData("pgap", new TagLib.ByteVector([1]), IntegerFlags);
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
        // The track tag is the bare position ("1", not "01/39"), so any count the source had is cleared. MP3 also
        // rewrites TRCK in ApplyMp3BookFrames, because TagLibSharp pads it to two digits.
        tag.TrackCount = 0;
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

    private static string? Text(string? value) => BookTags.Text(value);
}
