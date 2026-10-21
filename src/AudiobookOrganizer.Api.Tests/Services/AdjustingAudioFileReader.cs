using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

/// <summary>
/// The real reader, with each result passed through <paramref name="adjust"/> (path, what was read) so a test can
/// misreport one file or act at the moment it's read.
/// </summary>
internal sealed class AdjustingAudioFileReader(
    ChapterReader chapters, FfmpegRunner ffmpeg, Func<string, AudioFileMetadata, AudioFileMetadata> adjust)
    : AudioFileReader(chapters, ffmpeg)
{
    public override AudioFileMetadata Read(string path) => adjust(path, base.Read(path));
}
