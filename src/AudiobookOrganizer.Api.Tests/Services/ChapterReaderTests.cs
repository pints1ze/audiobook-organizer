using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class ChapterReaderTests : IDisposable
{
    // Expected values: the ffprobe -show_chapters output recorded in docs/fixtures/audio/README.md.
    private static readonly Chapter[] Expected =
    [
        new("Opening", TimeSpan.Zero),
        new("Middle", TimeSpan.FromSeconds(10)),
        new("Ending", TimeSpan.FromSeconds(20)),
    ];

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-chapters-{Guid.NewGuid():N}");

    public ChapterReaderTests() => Directory.CreateDirectory(_dir);

    internal static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", name);

    internal static ChapterReader NewReader() => new(FfmpegRunnerTests.Runner());

    [Theory]
    [InlineData("chapters.m4b")]
    [InlineData("chapters.mp3")]
    public void Read_returns_the_three_fixture_chapters(string fixture)
    {
        var chapters = NewReader().Read(Fixture(fixture));

        Assert.Equal(Expected, chapters);
    }

    [Fact]
    public void Read_returns_empty_for_an_mp3_without_chap_frames()
    {
        var path = CopyFixture("chapters.mp3");
        using (var file = TagLib.File.Create(path))
        {
            var tag = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
            tag.RemoveFrames("CHAP");
            tag.RemoveFrames("CTOC");
            file.Save();
        }

        Assert.Empty(NewReader().Read(path));
    }

    [Fact]
    public void Read_returns_empty_for_an_m4b_without_chapters()
    {
        // Remux the fixture with chapters dropped: no chpl atom and no QuickTime chapter track.
        var path = Path.Combine(_dir, "no-chapters.m4b");
        FfmpegRunnerTests.Runner().Ffmpeg(
            ["-v", "error", "-i", Fixture("chapters.m4b"), "-map", "0:a", "-c", "copy", "-map_chapters", "-1", path]);

        Assert.Empty(NewReader().Read(path));
    }

    [Theory]
    [InlineData("broken.m4b")]
    [InlineData("broken.mp3")]
    public void Read_throws_for_an_unreadable_file(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not audio");

        Assert.ThrowsAny<Exception>(() => NewReader().Read(path));
    }

    private string CopyFixture(string name)
    {
        var path = Path.Combine(_dir, name);
        File.Copy(Fixture(name), path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
