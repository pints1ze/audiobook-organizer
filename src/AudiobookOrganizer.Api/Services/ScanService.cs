using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Inventories every MP3 and M4B under the configured incoming directory into SQLite. Read-only on disk:
/// files are only enumerated, stat'ed and opened for reading. Unchanged files (same size and last-write time)
/// are not re-read; an unreadable file gets a row with ScanError and never ends the scan.
/// </summary>
public class ScanService(
    SettingsService settings,
    ScanRepository repository,
    AudioFileReader reader,
    ILogger<ScanService> logger)
{
    private static readonly string[] AudioExtensions = [".mp3", ".m4b"];

    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // skips symlinks and junctions
    };

    public ScanRun Run(CancellationToken cancellationToken)
    {
        var run = repository.InsertRun(Now());
        try
        {
            var incoming = settings.Get().IncomingPath;
            if (string.IsNullOrWhiteSpace(incoming))
                return Finish(run, ScanRun.Failed, "The incoming directory isn't set. Save it on the Settings page first.");
            if (!Directory.Exists(incoming))
                return Finish(run, ScanRun.Failed, $"The incoming directory '{incoming}' doesn't exist.");

            var root = Path.GetFullPath(incoming);
            var known = repository.GetFilesUnder(root).ToDictionary(f => f.SourcePath, StringComparer.Ordinal);
            var seenKnown = new HashSet<long>();
            var unchanged = new List<long>();

            foreach (var path in Directory.EnumerateFiles(root, "*", Enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;

                run.FilesFound++;
                known.TryGetValue(path, out var existing);
                if (existing is not null) seenKnown.Add(existing.Id);
                ScanFile(run, path, existing, unchanged);
            }

            repository.MarkSeen(unchanged, run.Id);
            run.FilesMissing = known.Count - seenKnown.Count;
            return Finish(run, ScanRun.Completed, null);
        }
        catch (OperationCanceledException)
        {
            return Finish(run, ScanRun.Failed, "The scan was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Scan {ScanId} failed", run.Id);
            return Finish(run, ScanRun.Failed, $"The incoming directory couldn't be read: {ex.Message}");
        }
    }

    private void ScanFile(ScanRun run, string path, AudioFile? existing, List<long> unchanged)
    {
        AudioFile row;
        try
        {
            var info = new FileInfo(path);
            row = new AudioFile
            {
                SourcePath = path,
                FileName = info.Name,
                Extension = info.Extension.TrimStart('.').ToLowerInvariant(),
                SizeBytes = info.Length,
                ModifiedAtUtc = info.LastWriteTimeUtc.ToString("O"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gone or unreadable between enumeration and stat: nothing reliable to record.
            run.FilesFailed++;
            logger.LogWarning(ex, "Couldn't read file info for {Path}", path);
            return;
        }

        if (existing is not null && existing.SizeBytes == row.SizeBytes && existing.ModifiedAtUtc == row.ModifiedAtUtc)
        {
            run.FilesUnchanged++;
            unchanged.Add(existing.Id);
            return;
        }

        try
        {
            var m = reader.Read(path);
            row.Title = m.Title;
            row.Album = m.Album;
            row.Artist = m.Artist;
            row.AlbumArtist = m.AlbumArtist;
            row.Composer = m.Composer;
            row.Genre = m.Genre;
            row.Year = m.Year;
            row.Track = m.Track;
            row.Disc = m.Disc;
            row.Comment = m.Comment;
            row.DurationMs = m.DurationMs;
            row.HasCover = m.HasCover;
            row.ChapterCount = m.ChapterCount;
        }
        catch (Exception ex)
        {
            // Any parser or ffprobe failure is recorded on the row; it must not end the scan.
            row.ScanError = ex.Message;
            repository.UpsertFile(row, run.Id);
            run.FilesFailed++;
            logger.LogWarning(ex, "Couldn't read {Path}", path);
            return;
        }

        repository.UpsertFile(row, run.Id);
        if (existing is null) run.FilesAdded++;
        else run.FilesUpdated++;
    }

    private ScanRun Finish(ScanRun run, string status, string? error)
    {
        run.Status = status;
        run.Error = error;
        run.FinishedAt = Now();
        repository.UpdateRun(run);
        return run;
    }

    private static string Now() => DateTime.UtcNow.ToString("O");
}
