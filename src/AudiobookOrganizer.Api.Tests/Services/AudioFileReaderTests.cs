using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class AudioFileReaderTests
{
    internal static AudioFileReader NewReader() =>
        new(ChapterReaderTests.NewReader(), FfmpegRunnerTests.Runner());

    [Theory]
    [InlineData("chapters.m4b")]
    [InlineData("chapters.mp3")]
    public void Read_returns_fixture_tags_duration_and_chapters(string fixture)
    {
        var metadata = NewReader().Read(ChapterReaderTests.Fixture(fixture));

        Assert.Equal("Fixture Book", metadata.Title);
        Assert.Equal("Fixture Author", metadata.Artist);
        Assert.InRange(metadata.DurationMs, 29_500, 30_500);
        Assert.Equal(3, metadata.ChapterCount);
        Assert.False(metadata.HasCover);
    }
}
