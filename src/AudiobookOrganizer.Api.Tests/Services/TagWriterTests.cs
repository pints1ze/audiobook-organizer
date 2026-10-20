using System.Security.Cryptography;
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
        TrackCount: 3,
        Disc: 1,
        Cover: Cover);

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

    private string Temp(string name = "out.mp3.partial") => Path.Combine(_dir, name);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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
        Assert.Equal((2u, 3u, 1u), (tag.Track, tag.TrackCount, tag.Disc));
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(TagLib.PictureType.FrontCover, picture.Type);
        Assert.Equal("image/jpeg", picture.MimeType);
        Assert.Equal(TagRoundTripTests.CoverJpeg, picture.Data.Data);
        Assert.Equal("Source comment", tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_m4b_copy_gets_every_tag_and_the_cover_and_keeps_chapters_audio_and_other_tags()
    {
        var source = M4bSource();
        var sourceHash = Hash(source);
        var before = TagRoundTripTests.Snapshot(source, TagRoundTripTests.Mp4AudioHash);
        var temp = Temp("out.m4b");

        _writer.Write(source, temp, Full);

        Assert.Equal(before, TagRoundTripTests.Snapshot(temp, TagRoundTripTests.Mp4AudioHash));
        Assert.Empty(Directory.GetFiles(_dir, "*.cover.*"));
        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        var tag = file.Tag;
        Assert.Equal("The Book", tag.Title);
        Assert.Equal("The Book", tag.Album);
        Assert.Equal(["Ann Author, Bob Writer"], tag.Performers);
        Assert.Equal(["Ann Author, Bob Writer"], tag.AlbumArtists);
        Assert.Equal(["Fantasy, Adventure"], tag.Genres);
        Assert.Equal(2001u, tag.Year);
        Assert.Equal((2u, 3u, 1u), (tag.Track, tag.TrackCount, tag.Disc));
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(TagRoundTripTests.CoverJpeg, picture.Data.Data);
        Assert.Equal("Source comment", tag.Comment);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void An_m4b_without_a_chosen_cover_keeps_the_sources_embedded_cover()
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
        var temp = Temp("out.m4b.partial");

        _writer.Write(source, temp, Full with { Cover = null });

        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        Assert.Equal(TagRoundTripTests.CoverJpeg, Assert.Single(file.Tag.Pictures).Data.Data);
        Assert.Equal("The Book", file.Tag.Title);
    }

    [Fact]
    public void An_m4b_with_no_cover_anywhere_gets_no_picture()
    {
        var temp = Temp("out.m4b.partial");

        _writer.Write(M4bSource(), temp, Full with { Cover = null });

        using var file = TagLib.File.Create(temp, "audio/mp4", TagLib.ReadStyle.Average);
        Assert.Empty(file.Tag.Pictures);
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

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
