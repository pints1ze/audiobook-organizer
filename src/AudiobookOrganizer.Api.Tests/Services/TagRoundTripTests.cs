using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

/// <summary>
/// Spike (slice 04): proves tags and a cover can be written to a copy of an audiobook without touching
/// its chapters or audio. MP3 is written with TagLibSharp; M4B with an ffmpeg stream-copy remux.
/// </summary>
public class TagRoundTripTests : IDisposable
{
    // 8x8 grey JPEG generated with ffmpeg.
    private static readonly byte[] CoverJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAPTGF2YzYzLjEuMTAyAP/bAEMACBQUFxQXGxsbGxsbIB4gISEhICAgICEhISQkJCoqKiQkJCEhJCQoKCoqLi8uKysqKy8vMjIyPDw5OUZGSFZWZ//EAEoAAQAAAAAAAAAAAAAAAAAAAAABAQAAAAAAAAAAAAAAAAAAAAAQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAAIAAgDASIAAhEAAxEA/9oADAMBAAIRAxEAPwAAD//Z");

    private readonly FfmpegRunner _ffmpeg = FfmpegRunnerTests.Runner();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-roundtrip-{Guid.NewGuid():N}");

    public TagRoundTripTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Writing_tags_and_cover_to_an_m4b_copy_with_ffmpeg_keeps_chapters_duration_and_audio()
    {
        var source = CopyFixture("chapters.m4b");
        var cover = Path.Combine(_dir, "cover.jpg");
        File.WriteAllBytes(cover, CoverJpeg);
        var tagged = Path.Combine(_dir, "tagged.m4b");
        var before = Snapshot(source, Mp4AudioHash);

        // -map 0:a, not -map 0: the mp4 muxer can't copy the source's chapter text track as a stream;
        // -map_chapters 0 rebuilds both chapter forms from the source's chapters instead.
        _ffmpeg.Ffmpeg(
        [
            "-v", "error", "-i", source, "-i", cover,
            "-map", "0:a", "-map", "1", "-c", "copy", "-map_chapters", "0",
            "-disposition:v:0", "attached_pic",
            "-metadata", "title=Test Title",
            "-metadata", "album=Test Album",
            "-metadata", "artist=Test Artist",
            "-metadata", "album_artist=Test Album Artist",
            "-metadata", "comment=Test comment",
            // Narrator-style key: ffmpeg's mp4 muxer has no freeform (----) atoms, and
            // -movflags use_metadata_tags drops the cover, so composer (©wrt) stands in for now.
            "-metadata", "composer=Test Narrator",
            tagged,
        ]);

        Assert.Equal(before, Snapshot(tagged, Mp4AudioHash));
        using var file = TagLib.File.Create(tagged);
        AssertCommonTags(file.Tag);
        Assert.Equal(["Test Narrator"], file.Tag.Composers);
    }

    [Fact]
    public void TagLibSharp_save_corrupts_m4b_chpl_atom()
    {
        // Known bug in TagLibSharp 2.3.0 (the reason M4B writes go through ffmpeg). If this test fails
        // after a TagLibSharp upgrade, the bug may be fixed: revisit the M4B write path.
        var path = CopyFixture("chapters.m4b");
        var original = ChplPayload(File.ReadAllBytes(path));

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Test Title";
            file.Save();
        }

        // Observed: the atom stays but loses its header and first chapter entry (24 bytes).
        var after = ChplPayload(File.ReadAllBytes(path));
        Assert.NotNull(after);
        Assert.NotEqual(original, after);
    }

    [Fact]
    public void Writing_tags_and_cover_to_an_mp3_copy_keeps_chapters_duration_and_audio()
    {
        var path = CopyFixture("chapters.mp3");
        var before = Snapshot(path, Mp3PayloadHash);

        using (var file = TagLib.File.Create(path))
        {
            WriteCommonTags(file.Tag);
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, "NARRATOR", create: true).Text = ["Test Narrator"];
            file.Save();
        }

        Assert.Equal(before, Snapshot(path, Mp3PayloadHash));
        using (var file = TagLib.File.Create(path))
        {
            AssertCommonTags(file.Tag);
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
            Assert.Equal(["Test Narrator"], TagLib.Id3v2.UserTextInformationFrame.Get(id3, "NARRATOR", false).Text);
        }
    }

    private static void WriteCommonTags(TagLib.Tag tag)
    {
        tag.Title = "Test Title";
        tag.Album = "Test Album";
        tag.Performers = ["Test Artist"];
        tag.AlbumArtists = ["Test Album Artist"];
        tag.Comment = "Test comment";
        tag.Pictures =
        [
            new TagLib.Picture(new TagLib.ByteVector(CoverJpeg))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/jpeg",
            },
        ];
    }

    private static void AssertCommonTags(TagLib.Tag tag)
    {
        Assert.Equal("Test Title", tag.Title);
        Assert.Equal("Test Album", tag.Album);
        Assert.Equal(["Test Artist"], tag.Performers);
        Assert.Equal(["Test Album Artist"], tag.AlbumArtists);
        Assert.Equal("Test comment", tag.Comment);
        var picture = Assert.Single(tag.Pictures);
        Assert.Equal(CoverJpeg, picture.Data.Data);
    }

    /// <summary>Duration is compared to the nearest 50 ms; chapters and the audio hash exactly.</summary>
    private record AudioSnapshot(string Chapters, long Duration50Ms, string AudioHash);

    private AudioSnapshot Snapshot(string path, Func<string, string> audioHash)
    {
        var chapters = ChapterReaderTests.NewReader().Read(path);
        Assert.Equal(3, chapters.Count);
        using var file = TagLib.File.Create(path);
        return new AudioSnapshot(
            string.Join(" | ", chapters),
            (long)Math.Round(file.Properties.Duration.TotalMilliseconds / 50),
            audioHash(path));
    }

    /// <summary>SHA-256 over the audio packets, as reported by ffmpeg's streamhash muxer.</summary>
    private string Mp4AudioHash(string path) =>
        _ffmpeg.Ffmpeg(["-v", "error", "-i", path, "-map", "0:a", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-"]).Trim();

    /// <summary>The payload of moov/udta/chpl (the Nero chapter atom), or null if absent.</summary>
    private static byte[]? ChplPayload(byte[] bytes)
    {
        var span = bytes.AsSpan();
        foreach (var type in new[] { "moov", "udta", "chpl" })
        {
            var box = FindChild(span, type);
            if (box is null) return null;
            span = span.Slice(box.Value.Start, box.Value.Length);
        }

        return span.ToArray();
    }

    private static (int Start, int Length)? FindChild(ReadOnlySpan<byte> container, string type)
    {
        var pos = 0;
        while (pos + 8 <= container.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(container[pos..]);
            if (size < 8 || pos + size > container.Length) return null;
            if (Encoding.ASCII.GetString(container.Slice(pos + 4, 4)) == type) return (pos + 8, size - 8);
            pos += size;
        }

        return null;
    }

    /// <summary>SHA-256 of the bytes between the ID3v2 tag and any trailing APE/ID3v1 tag.</summary>
    private static string Mp3PayloadHash(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var start = 0;
        if (bytes.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            var size = (bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9]; // syncsafe
            var hasFooter = (bytes[5] & 0x10) != 0;
            start = 10 + size + (hasFooter ? 10 : 0);
        }

        var end = bytes.Length;
        if (end - 128 >= start && bytes.AsSpan(end - 128, 3).SequenceEqual("TAG"u8)) end -= 128;
        if (end - 32 >= start && bytes.AsSpan(end - 32, 8).SequenceEqual("APETAGEX"u8))
        {
            var apeSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(end - 20)); // includes footer
            var hasHeader = (bytes[end - 9] & 0x80) != 0;
            end -= apeSize + (hasHeader ? 32 : 0);
        }

        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(start, end - start)));
    }

    private string CopyFixture(string name)
    {
        var path = Path.Combine(_dir, name);
        File.Copy(ChapterReaderTests.Fixture(name), path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
