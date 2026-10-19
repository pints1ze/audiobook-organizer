using System.Text.Json;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class GroupRepository(SqliteConnectionFactory connections)
{
    /// <summary>
    /// Files last seen in <paramref name="scanId"/> that were read without error and aren't in a user-corrected
    /// group: the grouping input.
    /// </summary>
    public IReadOnlyList<AudioFile> GetGroupableFiles(long scanId)
    {
        using var connection = connections.Open();
        return connection.Query<AudioFile>(
            """
            SELECT * FROM AudioFile a
            WHERE a.LastSeenScanId = @scanId AND a.ScanError IS NULL
                AND NOT EXISTS (SELECT 1 FROM FileGroupFile gf JOIN FileGroup g ON g.Id = gf.GroupId
                                WHERE gf.AudioFileId = a.Id AND g.UserCorrected = 1)
            """,
            new { scanId }).ToList();
    }

    /// <summary>Deletes every group the user hasn't corrected and inserts <paramref name="groups"/>, in one transaction.</summary>
    public void ReplaceProposed(IReadOnlyList<ProposedGroup> groups, long scanRunId)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM FileGroup WHERE UserCorrected = 0", transaction: transaction); // cascades to FileGroupFile

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

    private const string GroupColumns =
        "Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId, UserCorrected";

    /// <summary>Every group, the ones needing review first: Low, Medium, High, then by title.</summary>
    public IReadOnlyList<FileGroup> GetGroups()
    {
        using var connection = connections.Open();
        return connection.Query<FileGroup>(
            $"""
            SELECT {GroupColumns} FROM FileGroup
            ORDER BY CASE Confidence WHEN 'Low' THEN 0 WHEN 'Medium' THEN 1 ELSE 2 END, Title COLLATE NOCASE, Id
            """).ToList();
    }

    public FileGroup? GetGroup(long id)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<FileGroup>($"SELECT {GroupColumns} FROM FileGroup WHERE Id = @id", new { id });
    }

    /// <summary>Sets the group's title and author and marks it user-corrected. False when no group has <paramref name="id"/>.</summary>
    public bool UpdateTitleAuthor(long id, string? title, string? author)
    {
        using var connection = connections.Open();
        return connection.Execute(
            "UPDATE FileGroup SET Title = @title, Author = @author, UserCorrected = 1 WHERE Id = @id",
            new { id, title, author }) > 0;
    }

    /// <summary>The group's files in position order. Empty when the group is unknown or has no files.</summary>
    public IReadOnlyList<GroupFileRow> GetGroupFiles(long groupId)
    {
        using var connection = connections.Open();
        return connection.Query<GroupFileRow>(
            """
            SELECT gf.AudioFileId AS FileId, a.SourcePath, gf.Position, gf.Disc, gf.Track, a.DurationMs
            FROM FileGroupFile gf JOIN AudioFile a ON a.Id = gf.AudioFileId
            WHERE gf.GroupId = @groupId
            ORDER BY gf.Position, gf.AudioFileId
            """,
            new { groupId }).ToList();
    }

    /// <summary>
    /// Sets each file's position to its 1-based index in <paramref name="fileIds"/> and marks the group
    /// user-corrected, in one transaction. The caller has checked the ids are exactly the group's files.
    /// </summary>
    public void SetOrder(long groupId, IReadOnlyList<long> fileIds)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            "UPDATE FileGroupFile SET Position = @Position WHERE GroupId = @GroupId AND AudioFileId = @FileId",
            fileIds.Select((fileId, i) => new { GroupId = groupId, FileId = fileId, Position = i + 1 }),
            transaction);
        connection.Execute("UPDATE FileGroup SET UserCorrected = 1 WHERE Id = @groupId", new { groupId }, transaction);
        transaction.Commit();
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

/// <summary>One row of <see cref="GroupRepository.GetGroupFiles"/>.</summary>
public class GroupFileRow
{
    public long FileId { get; init; }
    public string SourcePath { get; init; } = "";
    public int Position { get; init; }
    public int? Disc { get; init; }
    public int? Track { get; init; }
    public long? DurationMs { get; init; }
}
