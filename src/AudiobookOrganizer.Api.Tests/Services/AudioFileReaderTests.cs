using AudiobookOrganizer.Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookOrganizer.Api.Tests.Services;

public class AudioFileReaderTests : IDisposable
{
    private readonly string _dir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"audiobook-organizer-reader-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static AudioFileReader NewReader() =>
        new(ChapterReaderTests.NewReader(), FfmpegRunnerTests.Runner(), NullLogger<AudioFileReader>.Instance);

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

    /// <summary>
    /// Writes a copy of chapters.mp3 to <paramref name="path"/> whose CTOC frame claims more child entries than it
    /// holds, so TagLibSharp can't parse the tag while ffprobe still reads it.
    /// </summary>
    internal static void WriteBrokenMp3(string path)
    {
        var bytes = File.ReadAllBytes(ChapterReaderTests.Fixture("chapters.mp3"));
        var ctoc = bytes.AsSpan().IndexOf("CTOC"u8);
        Assert.True(ctoc >= 0, "The fixture has no CTOC frame.");
        // Frame header (10 bytes), element id "toc" and its terminator, flags byte, then the entry count.
        var elementId = ctoc + 10;
        var count = Array.IndexOf(bytes, (byte)0, elementId) + 2;
        Assert.Equal(3, bytes[count]);
        bytes[count] = 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void Read_falls_back_to_ffprobe_when_TagLibSharp_cannot_parse_the_file()
    {
        var path = Path.Combine(_dir, "broken.mp3");
        WriteBrokenMp3(path);
        Assert.ThrowsAny<Exception>(() => TagLib.File.Create(path).Dispose());

        var logger = new ListLogger();
        var metadata = new AudioFileReader(ChapterReaderTests.NewReader(), FfmpegRunnerTests.Runner(), logger).Read(path);

        Assert.Equal("Fixture Book", metadata.Title);
        Assert.Equal("Fixture Author", metadata.Artist);
        Assert.InRange(metadata.DurationMs, 29_500, 30_500);
        Assert.Equal(3, metadata.ChapterCount);
        Assert.False(metadata.HasCover);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(path, warning.Message);
    }

    private sealed class ListLogger : ILogger<AudioFileReader>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void Read_throws_when_neither_TagLibSharp_nor_ffprobe_can_read_the_file()
    {
        var path = Path.Combine(_dir, "noise.mp3");
        var noise = new byte[4096];
        new Random(94).NextBytes(noise);
        File.WriteAllBytes(path, noise);

        Assert.ThrowsAny<Exception>(() => NewReader().Read(path));
    }
}
