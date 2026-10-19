using System.Text.Json;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class GroupRepository(SqliteConnectionFactory connections)
{
    /// <summary>Files last seen in <paramref name="scanId"/> that were read without error: the grouping input.</summary>
    public IReadOnlyList<AudioFile> GetGroupableFiles(long scanId)
    {
        using var connection = connections.Open();
        return connection.Query<AudioFile>(
            "SELECT * FROM AudioFile WHERE LastSeenScanId = @scanId AND ScanError IS NULL",
            new { scanId }).ToList();
    }

    /// <summary>Deletes every group and inserts <paramref name="groups"/>, all in one transaction.</summary>
    public void ReplaceAll(IReadOnlyList<ProposedGroup> groups, long scanRunId)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM FileGroup", transaction: transaction); // cascades to FileGroupFile

        foreach (var group in groups)
        {
            var groupId = connection.ExecuteScalar<long>(
                """
                INSERT INTO FileGroup (Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId)
                VALUES (@Title, @Author, @Confidence, @EvidenceJson, @FileCount, @TotalDurationMs, @ScanRunId)
                RETURNING Id
                """,
                new
                {
                    Title = group.EstimatedTitle,
                    Author = group.EstimatedAuthor,
                    Confidence = group.Confidence.ToString(),
                    EvidenceJson = JsonSerializer.Serialize(group.Evidence),
                    FileCount = group.Files.Count,
                    group.TotalDurationMs,
                    ScanRunId = scanRunId,
                },
                transaction);

            connection.Execute(
                """
                INSERT INTO FileGroupFile (GroupId, AudioFileId, Position, Disc, Track)
                VALUES (@GroupId, @AudioFileId, @Position, @Disc, @Track)
                """,
                group.Files.Select((f, i) => new { GroupId = groupId, AudioFileId = f.FileId, Position = i + 1, f.Disc, f.Track }),
                transaction);
        }

        transaction.Commit();
    }

    /// <summary>Every group, the ones needing review first: Low, Medium, High, then by title.</summary>
    public IReadOnlyList<FileGroup> GetGroups()
    {
        using var connection = connections.Open();
        return connection.Query<FileGroup>(
            """
            SELECT Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId FROM FileGroup
            ORDER BY CASE Confidence WHEN 'Low' THEN 0 WHEN 'Medium' THEN 1 ELSE 2 END, Title COLLATE NOCASE, Id
            """).ToList();
    }

    /// <summary>Files seen in <paramref name="scanId"/>, read without error, that are in no group.</summary>
    public IReadOnlyList<AudioFile> GetUngroupedFiles(long scanId)
    {
        using var connection = connections.Open();
        return connection.Query<AudioFile>(
            """
            SELECT * FROM AudioFile a
            WHERE a.LastSeenScanId = @scanId AND a.ScanError IS NULL
                AND NOT EXISTS (SELECT 1 FROM FileGroupFile g WHERE g.AudioFileId = a.Id)
            ORDER BY a.SourcePath
            """,
            new { scanId }).ToList();
    }
}
