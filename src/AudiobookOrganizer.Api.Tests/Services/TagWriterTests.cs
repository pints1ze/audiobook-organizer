using System.Security.Cryptography;
using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class TagWriterTests : IDisposable
{
    private static readonly TagCover Cover = new(TagRoundTripTests.CoverJpeg, "image/jpeg");

    private static readonly BookTags Full = new(
        Title: "The Book",
        Artists: ["Ann Author", "Bob Writer"],
        Genres: ["Fantasy", "Adventure"],
        Year: 2001,
        Track: 2,
        Disc: 1,
        Cover: Cover)
    {
        Narrators = ["Nora Narrator", "Ned Reader"],
        Subtitle = "A Subtitle",
        Series = "The Saga",
        SeriesPart = "2",
        Publisher = "Big Audio",
        ReleaseDate = "2001-05-04",
        Language = "english",
        Format = "unabridged",
        Isbn = "9780000000002",
        Asin = "B0TESTASIN",
    };

    /// <summary>A book with only the standard tags: none of the script's book fields.</summary>
    private static readonly BookTags Plain = new(
        Title: "The Book", Artists: ["Ann Author"], Genres: [], Year: null, Track: 1, Disc: null, Cover: null);

    private const string ITunes = "com.apple.iTunes";

    private static readonly TagLib.ByteVector MovementName = new([0xA9, (byte)'m', (byte)'v', (byte)'n']);
    private static readonly TagLib.ByteVector MovementNumber = new([0xA9, (byte)'m', (byte)'v', (byte)'i']);
    private static readonly TagLib.ByteVector Writer = new([0xA9, (byte)'w', (byte)'r', (byte)'t']);
    private static readonly TagLib.ByteVector Grouping = new([0xA9, (byte)'g', (byte)'r', (byte)'p']);

    private readonly TagWriter _writer = new(FfmpegRunnerTests.Runner());

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"audiobook-organizer-tagwriter-{Guid.NewGuid():N}");

    public TagWriterTests() => Directory.CreateDirectory(_dir);

    /// <summary>A copy of the fixture with a source genre, year and comment, so tests can tell what survives.</summary>
    private string Mp3Source()
    {
        var path = Path.Combine(_dir, "source.mp3");
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), path);
        using var file = TagLib.File.Create(path);
        file.Tag.Genres = ["Source Genre"];
        file.Tag.Year = 1999;
        file.Tag.Comment = "Source comment";
        file.Save();
        return path;
    }

    /// <summary>As <see cref="Mp3Source"/>, written with ffmpeg so the chapters stay intact.</summary>
    private string M4bSource()
    {
        var path = Path.Combine(_dir, "source.m4b");
        FfmpegRunnerTests.Runner().Ffmpeg(
        [
            "-v", "error", "-i", ChapterReaderTests.Fixture("chapters.m4b"), "-map", "0:a", "-c", "copy",
            "-map_chapters", "0", "-metadata", "genre=Source Genre", "-metadata", "date=1999",
            "-metadata", "comment=Source comment", path,
        ]);
        return path;
    }

    /// <summary>As <see cref="M4bSource"/>, with the fixture JPEG embedded as an attached picture.</summary>
    private string M4bSourceWithCover()
    {
        var plain = M4bSource();
        var cover = Path.Combine(_dir, "cover.jpg");
        File.WriteAllBytes(cover, TagRoundTripTests.CoverJpeg);
        var source = Path.Combine(_dir, "with-cover.m4b");
        FfmpegRunnerTests.Runner().Ffmpeg(
        [
            "-v", "error", "-i", plain, "-i", cover, "-map", "0:a", "-map", "1", "-c", "copy", "-map_chapters", "0",
            "-disposition:v:0", "attached_pic", source,
        ]);
        return source;
    }

    private string Temp(string name = "out.mp3.partial") => Path.Combine(_dir, name);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>The snapshot's duration and audio hash, for a copy that has no chapters to snapshot.</summary>
    private static (long Duration50Ms, string AudioHash) M4bAudio(string path)
    {
        using var file = TagLib.File.Create(path);
        return ((long)Math.Round(file.Properties.Duration.TotalMilliseconds / 50), TagRoundTripTests.Mp4AudioHash(path));
    }

    [Fact]
    public void An_mp3_copy_gets_every_tag_and_only_the_cover_and_keeps_chapters_audio_and_other_tags()
    {
        var source = Mp3Source();
        using (var withPicture = TagLib.File.Create(source))
        {
            withPicture.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector([1, 2, 3])) { Type = TagLib.PictureType.Other, MimeType = "image/png" }];
            withPicture.Save();
        }
        var sourceHash = Hash(source);
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp3PayloadHash);
        // Snapshot reads chapters by extension, so this target keeps one; the writer itself doesn't need it.
        var temp = Temp("out.mp3");

        _writer.Write(source, temp, Full);

        Assert.Equal(before, TagRoundTripTests.Snapshot(temp, TagRoundTripTests.Mp3PayloadHash));
        using var file = TagLib.File.Create(temp, "audio/mpeg", TagLib.ReadStyle.Average);
        var tag = file.Tag;
        Assert.Equal("The Book", tag.Title);
        Assert.Equal("The Book", tag.Album);
        Assert.Equal(["Ann Author", "Bob Writer"], tag.Performers);
        Assert.Equal(["Ann Author", "Bob Writer"], tag.AlbumArtists);
        Assert.Equal(["Fantasy", "Adventure"], tag.Genres);
        Assert.Equal(2001u, tag.Year);
        Assert.Equal((2u, 0u, 1u), (tag.Track, tag.TrackCount, tag.Disc));
        // The track frame is the bare position: no zero padding and no "/count".
        var trck = TagLib.Id3v2.TextInformationFrame.Get((TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2), "TRCK", false);
        Assert.NotNull(trck);
        Assert.Equal(["2"], trck.Text);
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(TagLib.PictureType.FrontCover, picture.Type);
        Assert.Equal("image/jpeg", picture.MimeType);
        Assert.Equal(TagRoundTripTests.CoverJpeg, picture.Data.Data);
        Assert.Equal("Source comment", tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_mp3_TagLibSharp_cannot_parse_is_remuxed_into_a_tagged_copy_with_its_chapters_and_audio()
    {
        var source = Path.Combine(_dir, "broken.mp3");
        AudioFileReaderTests.WriteBrokenMp3(source);
        Assert.ThrowsAny<Exception>(() => TagLib.File.Create(source).Dispose());
        var sourceHash = Hash(source);
        var temp = Temp("out.mp3");

        _writer.Write(source, temp, Full);

        using (var file = TagLib.File.Create(temp, "audio/mpeg", TagLib.ReadStyle.Average))
        {
            Assert.Equal("The Book", file.Tag.Title);
            Assert.Equal("Ann Author", file.Tag.FirstPerformer);
            Assert.Equal(2u, file.Tag.Track);
        }
        // Duration through ffprobe, as the job's read-back does: TagLibSharp misreads MPEG-2 MP3 duration.
        var read = AudioFileReaderTests.NewReader().Read(temp);
        Assert.InRange(read.DurationMs, 29_000, 31_000);
        Assert.Equal(3, read.ChapterCount);
        Assert.Equal(3, ChapterReaderTests.NewReader().Read(temp).Count);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_mp3_TagLibSharp_can_parse_is_a_byte_copy_without_ffmpeg()
    {
        var source = Mp3Source();
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp3PayloadHash);
        var temp = Temp("out.mp3");
        var withoutFfmpeg = new TagWriter(FfmpegRunnerTests.Runner(new ToolSettings { FfmpegPath = "no-such-ffmpeg-executable" }));

        withoutFfmpeg.Write(source, temp, Full);

        Assert.Equal(before, TagRoundTripTests.Snapshot(temp, TagRoundTripTests.Mp3PayloadHash));
    }

    [Fact]
    public void An_m4b_copy_has_no_chpl_and_gets_every_tag_and_the_cover_and_drops_chapters_and_keeps_audio_and_other_tags()
    {
        var source = M4bSource();
        Assert.NotNull(M4bFreeformSpikeTests.FindPath(File.ReadAllBytes(source), "moov", "udta", "chpl"));
        var sourceHash = Hash(source);
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp4AudioHash);
        var temp = Temp("out.m4b");

        _writer.Write(source, temp, Full);

        // M4B copies carry no chapters (2026-10-21 decision): neither chpl nor a QuickTime chapter track.
        Assert.Null(M4bFreeformSpikeTests.FindPath(File.ReadAllBytes(temp), "moov", "udta", "chpl"));
        Assert.Empty(ChapterReaderTests.NewReader().Read(temp));
        Assert.Equal((before.Duration50Ms, before.AudioHash), M4bAudio(temp));
        Assert.Empty(Directory.GetFiles(_dir, "*.cover.*"));
        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        var tag = file.Tag;
        Assert.Equal("The Book", tag.Title);
        Assert.Equal("The Book", tag.Album);
        Assert.Equal(["Ann Author, Bob Writer"], tag.Performers);
        Assert.Equal(["Ann Author, Bob Writer"], tag.AlbumArtists);
        Assert.Equal(["Fantasy, Adventure"], tag.Genres);
        Assert.Equal(2001u, tag.Year);
        Assert.Equal((2u, 0u, 1u), (tag.Track, tag.TrackCount, tag.Disc));
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(TagRoundTripTests.CoverJpeg, picture.Data.Data);
        Assert.Equal("Source comment", tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_m4b_without_a_chosen_cover_keeps_the_sources_embedded_cover()
    {
        var source = M4bSourceWithCover();
        var temp = Temp("out.m4b.partial");

        _writer.Write(source, temp, Full with { Cover = null });

        Assert.Null(M4bFreeformSpikeTests.FindPath(File.ReadAllBytes(temp), "moov", "udta", "chpl"));
        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        Assert.Equal(TagRoundTripTests.CoverJpeg, Assert.Single(file.Tag.Pictures).Data.Data);
        Assert.Equal("The Book", file.Tag.Title);
    }

    [Fact]
    public void An_m4b_chosen_cover_replaces_the_sources_embedded_cover()
    {
        var chosen = new TagCover([0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3], "image/png");
        var temp = Temp("out.m4b.partial");

        _writer.Write(M4bSourceWithCover(), temp, Full with { Cover = chosen });

        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        var picture = Assert.Single(file.Tag.Pictures);
        Assert.Equal(chosen.Image, picture.Data.Data);
        Assert.Equal("image/png", picture.MimeType);
    }

    [Fact]
    public void An_m4b_with_no_cover_anywhere_gets_no_picture()
    {
        var temp = Temp("out.m4b.partial");

        _writer.Write(M4bSource(), temp, Full with { Cover = null });

        Assert.Null(M4bFreeformSpikeTests.FindPath(File.ReadAllBytes(temp), "moov", "udta", "chpl"));
        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        Assert.Empty(file.Tag.Pictures);
        Assert.Equal("The Book", file.Tag.Title);
    }

    [Theory]
    [InlineData("source.mp3", "audio/mpeg")]
    [InlineData("source.m4b", "audio/mp4")]
    public void Empty_genres_and_a_null_year_leave_the_sources_values(string name, string mimeType)
    {
        var source = name.EndsWith(".mp3") ? Mp3Source() : M4bSource();
        var temp = Temp(name + ".partial");

        _writer.Write(source, temp, Full with { Genres = [], Year = null, Disc = null, Cover = null });

        using var file = TagLib.File.Create(temp, mimeType, TagLib.ReadStyle.Average);
        Assert.Equal(["Source Genre"], file.Tag.Genres);
        Assert.Equal(1999u, file.Tag.Year);
        Assert.Equal("The Book", file.Tag.Title);
    }

    [Theory]
    [InlineData("source.mp3")]
    [InlineData("source.m4b")]
    public void An_existing_temp_path_throws_and_is_left_alone(string name)
    {
        var source = name.EndsWith(".mp3") ? Mp3Source() : M4bSource();
        var temp = Temp(name + ".partial");
        File.WriteAllText(temp, "someone else's file");

        Assert.Throws<TagTargetExistsException>(() => _writer.Write(source, temp, Full));

        Assert.Equal("someone else's file", File.ReadAllText(temp));
        Assert.Empty(Directory.GetFiles(_dir, "*.cover.*"));
    }

    private static TagLib.Id3v2.Tag Id3(TagLib.File file) => (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, false);

    /// <summary>
    /// The text of the ID3v2 text frame with this id, or empty when there's none. TagLibSharp 2.3.0 parses only
    /// <c>T***</c> ids as text frames, so <c>MVNM</c> and <c>MVIN</c> come back as unknown frames and are decoded
    /// here from their bytes: an encoding byte, then the text.
    /// </summary>
    private static string[] Frame(TagLib.Id3v2.Tag tag, string id)
    {
        switch (tag.GetFrames(id).FirstOrDefault())
        {
            case TagLib.Id3v2.TextInformationFrame text:
                return text.Text;
            case TagLib.Id3v2.UnknownFrame unknown:
                var data = unknown.Data.Data;
                var encoding = data[0] switch
                {
                    0 => System.Text.Encoding.Latin1,
                    1 => System.Text.Encoding.Unicode, // UTF-16 with a BOM, which GetString skips below
                    2 => System.Text.Encoding.BigEndianUnicode,
                    _ => System.Text.Encoding.UTF8,
                };
                var body = data[1..];
                if (data[0] == 1 && body is [0xFE, 0xFF, ..]) encoding = System.Text.Encoding.BigEndianUnicode;
                if (data[0] == 1) body = body[2..];
                return encoding.GetString(body).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            default:
                return [];
        }
    }

    private static string? UserText(TagLib.Id3v2.Tag tag, string name) =>
        TagLib.Id3v2.UserTextInformationFrame.Get(tag, name, false)?.Text.Single();

    private static string? Url(TagLib.Id3v2.Tag tag, string id) =>
        tag.GetFrames<TagLib.Id3v2.UrlLinkFrame>(id).FirstOrDefault()?.Text.Single();

    private static TagLib.Mpeg4.AppleTag Apple(TagLib.File file) =>
        (TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, false);

    private static byte[]? Data(TagLib.Mpeg4.AppleTag apple, TagLib.ByteVector type) =>
        apple.DataBoxes(type).SingleOrDefault()?.Data.Data;

    [Fact]
    public void An_mp3_copy_gets_the_scripts_book_frames_and_keeps_chapters_and_payload()
    {
        var source = Mp3Source();
        var sourceHash = Hash(source);
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp3PayloadHash);
        var temp = Temp("out.mp3");

        _writer.Write(source, temp, Full);

        Assert.Equal(before, TagRoundTripTests.Snapshot(temp, TagRoundTripTests.Mp3PayloadHash));
        using var file = TagLib.File.Create(temp, "audio/mpeg", TagLib.ReadStyle.Average);
        var tag = Id3(file);
        Assert.Equal(["Nora Narrator", "Ned Reader"], Frame(tag, "TCOM"));
        Assert.Equal(["A Subtitle"], Frame(tag, "TIT3"));
        Assert.Equal("The Saga", UserText(tag, "SERIES"));
        Assert.Equal("2", UserText(tag, "SERIES-PART"));
        Assert.Equal(["The Saga"], Frame(tag, "MVNM"));
        Assert.Equal(["2"], Frame(tag, "MVIN"));
        Assert.Equal("1", UserText(tag, "SHOWMOVEMENT"));
        Assert.Equal(["The Saga, Book #2"], Frame(tag, "TIT1"));
        Assert.Equal(["The Saga 2 - The Book"], Frame(tag, "TSOA"));
        Assert.Equal(["english"], Frame(tag, "TLAN"));
        Assert.Equal(["Big Audio"], Frame(tag, "TPUB"));
        Assert.Equal("unabridged", UserText(tag, "FORMAT"));
        Assert.Equal("9780000000002", UserText(tag, "ISBN"));
        Assert.Equal("B0TESTASIN", UserText(tag, "ASIN"));
        Assert.Equal(["2001-05-04"], Frame(tag, "TDRL"));
        Assert.Equal("https://www.audible.com/pd/B0TESTASIN", Url(tag, "WOAF"));
        Assert.Equal("Source comment", file.Tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_m4b_copy_gets_the_scripts_book_atoms_has_no_chpl_and_drops_chapters_and_keeps_audio()
    {
        var source = M4bSource();
        var sourceHash = Hash(source);
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp4AudioHash);
        var temp = Temp("out.m4b");

        _writer.Write(source, temp, Full);

        Assert.Null(M4bFreeformSpikeTests.FindPath(File.ReadAllBytes(temp), "moov", "udta", "chpl"));
        Assert.Empty(ChapterReaderTests.NewReader().Read(temp));
        Assert.Equal((before.Duration50Ms, before.AudioHash), M4bAudio(temp));
        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        var apple = Apple(file);
        Assert.Equal(["Nora Narrator, Ned Reader"], apple.GetText(Writer));
        Assert.Equal("A Subtitle", apple.GetDashBox(ITunes, "SUBTITLE"));
        Assert.Equal("The Saga", apple.GetDashBox(ITunes, "SERIES"));
        Assert.Equal("2", apple.GetDashBox(ITunes, "SERIES-PART"));
        Assert.Equal(["The Saga"], apple.GetText(MovementName));
        Assert.Equal([0, 2], Data(apple, MovementNumber));
        Assert.Equal([1], Data(apple, "shwm"));
        Assert.Equal(["The Saga, Book #2"], apple.GetText(Grouping));
        Assert.Equal(["The Saga 2 - The Book"], apple.GetText("soal"));
        Assert.Equal("english", apple.GetDashBox(ITunes, "LANGUAGE"));
        Assert.Equal("Big Audio", apple.GetDashBox(ITunes, "PUBLISHER"));
        Assert.Equal("unabridged", apple.GetDashBox(ITunes, "FORMAT"));
        Assert.Equal("9780000000002", apple.GetDashBox(ITunes, "ISBN"));
        Assert.Equal("B0TESTASIN", apple.GetDashBox(ITunes, "ASIN"));
        Assert.Equal("2001-05-04", apple.GetDashBox(ITunes, "RELEASETIME"));
        Assert.Equal("https://www.audible.com/pd/B0TESTASIN", apple.GetDashBox(ITunes, "WWWAUDIOFILE"));
        Assert.Equal([2], Data(apple, "stik"));
        Assert.Equal([1], Data(apple, "pgap"));
        Assert.Equal("Source comment", file.Tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_mp3_book_without_the_scripts_fields_writes_none_of_their_frames_and_sorts_by_title()
    {
        var temp = Temp("out.mp3");

        _writer.Write(Mp3Source(), temp, Plain);

        using var file = TagLib.File.Create(temp, "audio/mpeg", TagLib.ReadStyle.Average);
        var tag = Id3(file);
        foreach (var id in new[] { "TCOM", "TIT3", "MVNM", "MVIN", "TIT1", "TLAN", "TPUB", "TDRL" })
            Assert.Empty(Frame(tag, id));
        Assert.Null(Url(tag, "WOAF"));
        foreach (var name in new[] { "SERIES", "SERIES-PART", "SHOWMOVEMENT", "FORMAT", "ISBN", "ASIN" })
            Assert.Null(UserText(tag, name));
        Assert.Equal(["The Book"], Frame(tag, "TSOA"));
    }

    [Fact]
    public void An_m4b_book_without_the_scripts_fields_writes_none_of_their_atoms_and_sorts_by_title()
    {
        var temp = Temp("out.m4b");

        _writer.Write(M4bSource(), temp, Plain);

        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        var apple = Apple(file);
        foreach (var name in new[] { "SUBTITLE", "SERIES", "SERIES-PART", "LANGUAGE", "PUBLISHER", "FORMAT", "ISBN", "ASIN", "RELEASETIME", "WWWAUDIOFILE" })
            Assert.Null(apple.GetDashBox(ITunes, name));
        Assert.Empty(apple.GetText(Writer));
        Assert.Empty(apple.GetText(MovementName));
        Assert.Null(Data(apple, MovementNumber));
        Assert.Null(Data(apple, "shwm"));
        Assert.Empty(apple.GetText(Grouping));
        Assert.Equal(["The Book"], apple.GetText("soal"));
        Assert.Equal([2], Data(apple, "stik"));
        Assert.Equal([1], Data(apple, "pgap"));
    }

    [Fact]
    public void Without_a_series_a_subtitle_sorts_the_album_as_title_dash_subtitle()
    {
        var temp = Temp("out.mp3");

        _writer.Write(Mp3Source(), temp, Plain with { Subtitle = "A Subtitle" });

        using var file = TagLib.File.Create(temp, "audio/mpeg", TagLib.ReadStyle.Average);
        Assert.Equal(["The Book - A Subtitle"], Frame(Id3(file), "TSOA"));
    }

    [Theory]
    [InlineData("source.mp3", "audio/mpeg")]
    [InlineData("source.m4b", "audio/mp4")]
    public void A_series_without_a_part_is_the_content_group_and_sorts_before_the_title(string name, string mimeType)
    {
        var source = name.EndsWith(".mp3") ? Mp3Source() : M4bSource();
        var temp = Temp("out" + Path.GetExtension(name));

        _writer.Write(source, temp, Plain with { Series = "The Saga" });

        using var file = TagLib.File.Create(temp, mimeType, TagLib.ReadStyle.Average);
        Assert.Equal("The Saga", file.Tag.Grouping);
        Assert.Equal("The Saga - The Book", file.Tag.AlbumSort);
    }

    [Fact]
    public void A_fractional_series_part_is_text_on_mp3_but_gets_no_m4b_movement_number()
    {
        var mp3 = Temp("out.mp3");
        var m4b = Temp("out.m4b");
        var tags = Plain with { Series = "The Saga", SeriesPart = "1.5" };

        _writer.Write(Mp3Source(), mp3, tags);
        _writer.Write(M4bSource(), m4b, tags);

        using (var file = TagLib.File.Create(mp3, "audio/mpeg", TagLib.ReadStyle.Average))
        {
            Assert.Equal("1.5", UserText(Id3(file), "SERIES-PART"));
            Assert.Equal(["1.5"], Frame(Id3(file), "MVIN"));
        }
        using (var file = TagLib.File.Create(m4b, "audio/mp4", TagLib.ReadStyle.Average))
        {
            var apple = Apple(file);
            Assert.Equal("1.5", apple.GetDashBox(ITunes, "SERIES-PART"));
            Assert.Null(Data(apple, MovementNumber));
            Assert.Equal(["The Saga"], apple.GetText(MovementName));
            Assert.Equal("The Saga, Book #1.5", file.Tag.Grouping);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
