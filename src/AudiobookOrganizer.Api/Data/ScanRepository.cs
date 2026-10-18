using AudiobookOrganizer.Api.Models.Local;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class ScanRepository(SqliteConnectionFactory connections)
{
    private const string RunColumns =
        "Id, StartedAt, FinishedAt, Status, FilesFound, FilesAdded, FilesUpdated, FilesUnchanged, FilesMissing, FilesFailed, Error";

    public ScanRun InsertRun(string startedAt)
    {
        using var connection = connections.Open();
        return connection.QuerySingle<ScanRun>(
            $"INSERT INTO ScanRun (StartedAt, Status) VALUES (@startedAt, @status) RETURNING {RunColumns}",
            new { startedAt, status = ScanRun.Running });
    }

    public ScanRun? GetRun(long id)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<ScanRun>($"SELECT {RunColumns} FROM ScanRun WHERE Id = @id", new { id });
    }

    /// <summary>Writes the run's status, counts, error and finish time.</summary>
    public void UpdateRun(ScanRun run)
    {
        using var connection = connections.Open();
        connection.Execute(
            """
            UPDATE ScanRun SET FinishedAt = @FinishedAt, Status = @Status, FilesFound = @FilesFound,
                FilesAdded = @FilesAdded, FilesUpdated = @FilesUpdated, FilesUnchanged = @FilesUnchanged,
                FilesMissing = @FilesMissing, FilesFailed = @FilesFailed, Error = @Error
            WHERE Id = @Id
            """,
            run);
    }

    /// <summary>Rows whose SourcePath starts with <paramref name="directory"/> plus a separator (exact, case-sensitive match).</summary>
    public IReadOnlyList<AudioFile> GetFilesUnder(string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        using var connection = connections.Open();
        return connection.Query<AudioFile>(
            "SELECT * FROM AudioFile WHERE substr(SourcePath, 1, length(@prefix)) = @prefix",
            new { prefix }).ToList();
    }

    /// <summary>Inserts or replaces a file's row by SourcePath in one statement. Id and FirstSeenScanId stay as they were.</summary>
    public void UpsertFile(AudioFile file, long scanId)
    {
        using var connection = connections.Open();
        connection.Execute(
            """
            INSERT INTO AudioFile (SourcePath, FileName, Extension, SizeBytes, ModifiedAtUtc, DurationMs, Title, Album,
                Artist, AlbumArtist, Composer, Genre, Year, Track, Disc, Comment, HasCover, ChapterCount, ScanError,
                FirstSeenScanId, LastSeenScanId)
            VALUES (@SourcePath, @FileName, @Extension, @SizeBytes, @ModifiedAtUtc, @DurationMs, @Title, @Album,
                @Artist, @AlbumArtist, @Composer, @Genre, @Year, @Track, @Disc, @Comment, @HasCover, @ChapterCount, @ScanError,
                @ScanId, @ScanId)
            ON CONFLICT(SourcePath) DO UPDATE SET FileName = excluded.FileName, Extension = excluded.Extension,
                SizeBytes = excluded.SizeBytes, ModifiedAtUtc = excluded.ModifiedAtUtc, DurationMs = excluded.DurationMs,
                Title = excluded.Title, Album = excluded.Album, Artist = excluded.Artist, AlbumArtist = excluded.AlbumArtist,
                Composer = excluded.Composer, Genre = excluded.Genre, Year = excluded.Year, Track = excluded.Track,
                Disc = excluded.Disc, Comment = excluded.Comment, HasCover = excluded.HasCover,
                ChapterCount = excluded.ChapterCount, ScanError = excluded.ScanError, LastSeenScanId = excluded.LastSeenScanId
            """,
            new
            {
                file.SourcePath, file.FileName, file.Extension, file.SizeBytes, file.ModifiedAtUtc, file.DurationMs,
                file.Title, file.Album, file.Artist, file.AlbumArtist, file.Composer, file.Genre, file.Year, file.Track,
                file.Disc, file.Comment, file.HasCover, file.ChapterCount, file.ScanError, ScanId = scanId,
            });
    }

    /// <summary>Sets LastSeenScanId on unchanged files, in chunks inside one transaction.</summary>
    public void MarkSeen(IReadOnlyCollection<long> ids, long scanId)
    {
        if (ids.Count == 0) return;
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var chunk in ids.Chunk(500))
        {
            connection.Execute("UPDATE AudioFile SET LastSeenScanId = @scanId WHERE Id IN @chunk",
                new { scanId, chunk }, transaction);
        }
        transaction.Commit();
    }
}
