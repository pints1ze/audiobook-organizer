using System.Buffers.Binary;
using System.Text;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

/// <summary>
/// Spike (slice 60): can the developer's Mp3tag fields that M4B keeps in freeform <c>----:com.apple.iTunes:</c> atoms
/// (and the iTunes atoms ffmpeg can't write) be added after the slice 56 ffmpeg remux without breaking chapters,
/// audio, cover or the standard tags? Each test starts from a temp copy of the fixture, remuxed by <see cref="TagWriter"/>.
/// Findings are recorded in docs/fixtures/audio/README.md and slice 60's Test results.
/// </summary>
public class M4bFreeformSpikeTests : IDisposable
{
    private const string ITunes = "com.apple.iTunes";

    private static readonly (string Name, string Value)[] Freeform =
    [
        ("SERIES", "Test Series"),
        ("SERIES-PART", "2"),
        ("ASIN", "B0TESTASIN"),
        ("SUBTITLE", "Test Subtitle"),
    ];

    private static readonly TagLib.ByteVector MovementName = new([0xA9, (byte)'m', (byte)'v', (byte)'n']);
    private static readonly TagLib.ByteVector MovementNumber = new([0xA9, (byte)'m', (byte)'v', (byte)'i']);
    private static readonly TagLib.ByteVector MediaKind = "stik";
    private static readonly TagLib.ByteVector Gapless = "pgap";
    private static readonly TagLib.ByteVector Advisory = "rtng";

    // Integer data boxes carry the "signed integer" well-known type (21), which TagLibSharp names ForTempo.
    private const uint IntegerFlags = (uint)TagLib.Mpeg4.AppleDataBox.FlagType.ForTempo;

    private readonly FfmpegRunner _ffmpeg = FfmpegRunnerTests.Runner();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-freeform-{Guid.NewGuid():N}");

    public M4bFreeformSpikeTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void TagLibSharp_save_of_one_freeform_atom_cuts_the_first_24_bytes_of_the_chpl_payload()
    {
        var remuxed = Remux();
        var before = File.ReadAllBytes(remuxed);
        var path = Candidate(remuxed, "saved.m4b");

        using (var file = TagLib.File.Create(path))
        {
            Apple(file).SetDashBox(ITunes, "SERIES", "Test Series");
            file.Save();
        }

        var after = File.ReadAllBytes(path);
        var original = Payload(before, Chpl(before));
        var damaged = Payload(after, Chpl(after));
        Assert.Equal(55, original.Length); // version 1 + flags, 4 reserved bytes, count 3, then 3 entries
        Assert.Equal(1, original[0]);
        Assert.Equal(3, original[8]);
        // Observed: the header is rewritten with the new size (63 -> 39) and the payload loses its first 24 bytes:
        // version, flags, reserved, count, and the first entry's start time, title length and "Openin". The rest
        // ("g" plus the Middle and Ending entries) is kept byte for byte.
        Assert.Equal(original[24..], damaged);
        // The box also moves: TagLibSharp rewrites meta with 2,048 bytes of free padding and puts chpl after it.
        Assert.NotEqual(Chpl(before)[^1].Start, Chpl(after)[^1].Start);

        // ffprobe still reports the 3 chapters, but from the QuickTime chapter text track (it overrides chpl's).
        Assert.Equal(Chapters(remuxed), Chapters(path));
        // Read alone (the audio track's tref/chap disabled), the original chpl gives the 3 chapters in chpl's
        // 1/10000000 time base; the damaged one gives none.
        Assert.Equal(
            [
                "Opening@0 (1/10000000)",
                "Middle@100000000 (1/10000000)",
                "Ending@200000000 (1/10000000)",
            ],
            ChplOnlyChapters(remuxed));
        Assert.Empty(ChplOnlyChapters(path));
    }

    [Fact]
    public void Candidate_A_TagLibSharp_write_then_restoring_chpl_keeps_everything()
    {
        var remuxed = Remux();
        var path = Candidate(remuxed, "candidate-a.m4b");
        WriteExtendedTags(path);

        var bytes = File.ReadAllBytes(path);
        var remuxedBytes = File.ReadAllBytes(remuxed);
        var originalChpl = BoxBytes(remuxedBytes, Chpl(remuxedBytes));
        var chain = Chpl(bytes);
        // The restored box is 24 bytes longer than the damaged one, so moov and udta grow. That's only a byte copy
        // plus two size fields because moov comes after mdat: no chunk offset (stco) points past it.
        Assert.Equal(originalChpl.Length - 24, chain[^1].Size);
        Assert.True(Top(bytes, "moov").Start > Top(bytes, "mdat").Start);
        File.WriteAllBytes(path, Splice(bytes, chain, originalChpl));

        var restored = File.ReadAllBytes(path);
        Assert.Equal(originalChpl, BoxBytes(restored, Chpl(restored)));
        AssertKeeps(remuxed, path);
        AssertExtendedTags(path);
        Assert.Equal(ChplOnlyChapters(remuxed), ChplOnlyChapters(path));
        KeepForManualCheck(path);
    }

    [Fact]
    public void Candidate_B_TagLibSharp_write_then_dropping_chpl_keeps_chapters_only_in_the_chapter_track()
    {
        var remuxed = Remux();
        var path = Candidate(remuxed, "candidate-b.m4b");
        WriteExtendedTags(path);

        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, Splice(bytes, Chpl(bytes), []));

        Assert.Null(FindPath(File.ReadAllBytes(path), "moov", "udta", "chpl"));
        // ffprobe still reads the 3 chapters, from the QuickTime chapter track; players that read only chpl see none.
        AssertKeeps(remuxed, path);
        AssertExtendedTags(path);
        Assert.Empty(ChplOnlyChapters(path));
    }

    [Fact]
    public void Candidate_C_ffmpeg_use_metadata_tags_writes_mdta_keys_TagLibSharp_cannot_read_and_drops_the_cover()
    {
        var remuxed = Remux();
        var path = Path.Combine(_dir, "candidate-c.m4b");
        List<string> arguments =
        [
            "-v", "error", "-i", remuxed, "-map", "0:a", "-map", "0:v?", "-c", "copy", "-map_chapters", "0",
            "-map_metadata", "0", "-disposition:v:0", "attached_pic", "-movflags", "use_metadata_tags",
        ];
        foreach (var (name, value) in Freeform) arguments.AddRange(["-metadata", $"{name}={value}"]);
        arguments.AddRange(["-f", "ipod", path]);
        _ffmpeg.Ffmpeg(arguments);

        var bytes = File.ReadAllBytes(path);
        // Observed (ffmpeg 9.0.2): the keys come out under an mdta handler with a keys box, not as ---- atoms...
        Assert.NotNull(FindPath(bytes, "moov", "udta", "meta", "keys"));
        var hdlr = FindPath(bytes, "moov", "udta", "meta", "hdlr")!;
        Assert.Equal("mdta", Encoding.ASCII.GetString(bytes, hdlr[^1].Start + 16, 4));
        // ...ffprobe reads them...
        var format = _ffmpeg.Ffprobe(["-v", "error", "-show_entries", "format_tags", "-of", "compact", path]);
        Assert.Contains("tag:SERIES=Test Series", format);
        Assert.Contains("tag:ASIN=B0TESTASIN", format);
        // ...but TagLibSharp reads nothing from them, not even the standard tags, and the cover is gone.
        using (var file = TagLib.File.Create(path))
        {
            Assert.True(string.IsNullOrEmpty(file.Tag.Title));
            Assert.Empty(file.Tag.Pictures);
            Assert.Null(Apple(file).GetDashBox(ITunes, "SERIES"));
        }
        Assert.DoesNotContain("attached_pic=1", _ffmpeg.Ffprobe(["-v", "error", "-show_streams", "-of", "compact", path]));
        // Chapters, chpl and audio do survive.
        Assert.Equal(Chapters(remuxed), Chapters(path));
        var remuxedBytes = File.ReadAllBytes(remuxed);
        Assert.Equal(BoxBytes(remuxedBytes, Chpl(remuxedBytes)), BoxBytes(bytes, Chpl(bytes)));
        Assert.Equal(TagRoundTripTests.Mp4AudioHash(remuxed), TagRoundTripTests.Mp4AudioHash(path));
    }

    /// <summary>The slice 56 remux of the fixture: standard tags and the cover, through the production writer.</summary>
    private string Remux()
    {
        var source = Path.Combine(_dir, "source.m4b");
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), source);
        var remuxed = Path.Combine(_dir, "remuxed.m4b");
        new TagWriter(_ffmpeg).Write(source, remuxed, new BookTags(
            "Test Title", ["Test Author"], ["Fantasy"], 2020, 1, 1, null,
            new TagCover(TagRoundTripTests.CoverJpeg, "image/jpeg")));
        return remuxed;
    }

    private string Candidate(string remuxed, string name)
    {
        var path = Path.Combine(_dir, name);
        File.Copy(remuxed, path);
        return path;
    }

    private static TagLib.Mpeg4.AppleTag Apple(TagLib.File file) =>
        (TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, create: true);

    private static void WriteExtendedTags(string path)
    {
        using var file = TagLib.File.Create(path);
        var apple = Apple(file);
        foreach (var (name, value) in Freeform) apple.SetDashBox(ITunes, name, value);
        apple.SetText(MovementName, "Test Series");
        apple.SetData(MovementNumber, new TagLib.ByteVector([0, 2]), IntegerFlags);
        apple.SetData(MediaKind, new TagLib.ByteVector([2]), IntegerFlags); // 2 = Audiobook
        apple.SetData(Gapless, new TagLib.ByteVector([1]), IntegerFlags);
        apple.SetData(Advisory, new TagLib.ByteVector([2]), IntegerFlags); // 2 = clean, as the script writes it
        file.Save();
    }

    private static void AssertExtendedTags(string path)
    {
        using var file = TagLib.File.Create(path);
        var apple = Apple(file);
        foreach (var (name, value) in Freeform) Assert.Equal(value, apple.GetDashBox(ITunes, name));
        Assert.Equal(["Test Series"], apple.GetText(MovementName));
        Assert.Equal([0, 2], Data(apple, MovementNumber));
        Assert.Equal([2], Data(apple, MediaKind));
        Assert.Equal([1], Data(apple, Gapless));
        Assert.Equal([2], Data(apple, Advisory));
    }

    private static byte[] Data(TagLib.Mpeg4.AppleTag apple, TagLib.ByteVector type) =>
        Assert.Single(apple.DataBoxes(type)).Data.Data;

    /// <summary>Chapters (ffprobe), audio streamhash, cover and the remux's standard tags are as in the remuxed copy.</summary>
    private static void AssertKeeps(string remuxed, string path)
    {
        Assert.Equal(3, Chapters(path).Count);
        Assert.Equal(Chapters(remuxed), Chapters(path));
        Assert.Equal(TagRoundTripTests.Mp4AudioHash(remuxed), TagRoundTripTests.Mp4AudioHash(path));
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        Assert.Equal("Test Title", tag.Title);
        Assert.Equal("Test Title", tag.Album);
        Assert.Equal(["Test Author"], tag.Performers);
        Assert.Equal(["Test Author"], tag.AlbumArtists);
        Assert.Equal(["Fantasy"], tag.Genres);
        Assert.Equal(2020u, tag.Year);
        Assert.Equal(1u, tag.Track);
        Assert.Equal(1u, tag.TrackCount);
        Assert.Equal(TagRoundTripTests.CoverJpeg, Assert.Single(tag.Pictures).Data.Data);
    }

    private static IReadOnlyList<Chapter> Chapters(string path) => ChapterReaderTests.NewReader().Read(path);

    /// <summary>
    /// The chapters ffprobe reads from chpl alone, as "title@start (time base)": a copy with the audio track's
    /// <c>tref/chap</c> renamed, so ffmpeg doesn't find the chapter text track (whose chapters otherwise replace chpl's).
    /// </summary>
    private List<string> ChplOnlyChapters(string path)
    {
        var bytes = File.ReadAllBytes(path);
        foreach (var trak in Children(bytes, Top(bytes, "moov")).Where(b => b.Type == "trak"))
            if (FindIn(bytes, trak, "tref") is { } tref && FindIn(bytes, tref, "chap") is { } chap)
                Encoding.ASCII.GetBytes("xchp").CopyTo(bytes, chap.Start + 4);
        var copy = Path.Combine(_dir, $"chpl-only-{Guid.NewGuid():N}.m4b");
        File.WriteAllBytes(copy, bytes);
        var lines = _ffmpeg.Ffprobe(["-v", "error", "-show_entries", "chapter=start,time_base:chapter_tags=title", "-of", "compact", copy]);
        return lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|').Skip(1).Select(f => f.Split('=', 2)).ToDictionary(f => f[0], f => f[1]))
            .Select(f => $"{f["tag:title"]}@{f["start"]} ({f["time_base"]})")
            .ToList();
    }

    /// <summary>Copies candidate A's output to SPIKE_KEEP_DIR (e.g. the git-ignored data/ folder) for the manual checks.</summary>
    private static void KeepForManualCheck(string path)
    {
        if (Environment.GetEnvironmentVariable("SPIKE_KEEP_DIR") is { Length: > 0 } dir)
            File.Copy(path, Path.Combine(dir, "slice60-candidate-a.m4b"), overwrite: true);
    }

    // A minimal ISO BMFF box reader: enough to find moov/udta/chpl and moov/udta/meta/ilst. 32-bit sizes only.
    private record Box(string Type, int Start, int Size);

    private static List<Box> Chpl(byte[] bytes) =>
        FindPath(bytes, "moov", "udta", "chpl") ?? throw new InvalidOperationException("No moov/udta/chpl box.");

    private static byte[] BoxBytes(byte[] bytes, List<Box> chain) => bytes[chain[^1].Start..(chain[^1].Start + chain[^1].Size)];

    private static byte[] Payload(byte[] bytes, List<Box> chain) => BoxBytes(bytes, chain)[8..];

    private static Box Top(byte[] bytes, string type) => Children(bytes, null).First(b => b.Type == type);

    /// <summary>The chain of boxes from the top level down to the last type in the path, or null.</summary>
    private static List<Box>? FindPath(byte[] bytes, params string[] path)
    {
        List<Box> chain = [];
        Box? parent = null;
        foreach (var type in path)
        {
            var box = parent is null ? Children(bytes, null).FirstOrDefault(b => b.Type == type) : FindIn(bytes, parent, type);
            if (box is null) return null;
            chain.Add(box);
            parent = box;
        }

        return chain;
    }

    private static Box? FindIn(byte[] bytes, Box parent, string type) => Children(bytes, parent).FirstOrDefault(b => b.Type == type);

    private static IEnumerable<Box> Children(byte[] bytes, Box? parent)
    {
        // meta is a full box: 4 bytes of version and flags come before its children.
        var pos = parent is null ? 0 : parent.Start + (parent.Type == "meta" ? 12 : 8);
        var end = parent is null ? bytes.Length : parent.Start + parent.Size;
        while (pos + 8 <= end)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos));
            if (size < 8 || pos + size > end) yield break; // 64-bit and to-end sizes aren't needed for the fixture
            yield return new Box(Encoding.Latin1.GetString(bytes, pos + 4, 4), pos, size);
            pos += size;
        }
    }

    /// <summary>Replaces the last box of <paramref name="chain"/> with <paramref name="replacement"/> and fixes every ancestor's size.</summary>
    private static byte[] Splice(byte[] bytes, List<Box> chain, byte[] replacement)
    {
        var target = chain[^1];
        var delta = replacement.Length - target.Size;
        var result = new byte[bytes.Length + delta];
        bytes.AsSpan(0, target.Start).CopyTo(result);
        replacement.CopyTo(result.AsSpan(target.Start));
        bytes.AsSpan(target.Start + target.Size).CopyTo(result.AsSpan(target.Start + replacement.Length));
        foreach (var ancestor in chain.Take(chain.Count - 1))
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(ancestor.Start), (uint)(ancestor.Size + delta));
        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
