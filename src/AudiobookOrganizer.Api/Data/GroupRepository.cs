using System.Text.Json;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;
using Dapper;
using Microsoft.Data.Sqlite;

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

    /// <summary>The subset of <paramref name="fileIds"/> that are files last seen in <paramref name="scanId"/> and read without error.</summary>
    public IReadOnlySet<long> GetMovableFileIds(long scanId, IReadOnlyList<long> fileIds)
    {
        using var connection = connections.Open();
        return connection.Query<long>(
            "SELECT Id FROM AudioFile WHERE Id IN @fileIds AND LastSeenScanId = @scanId AND ScanError IS NULL",
            new { scanId, fileIds }).ToHashSet();
    }

    /// <summary>
    /// Moves <paramref name="fileIds"/> out of their groups and appends them, in order, to <paramref name="targetGroupId"/>,
    /// in one transaction. The caller has checked the ids are movable and not already in the target.
    /// </summary>
    public void MoveFiles(long targetGroupId, IReadOnlyList<long> fileIds)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var last = connection.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(Position), 0) FROM FileGroupFile WHERE GroupId = @targetGroupId",
            new { targetGroupId }, transaction);
        PlaceFiles(connection, transaction, targetGroupId, fileIds, last);
        transaction.Commit();
    }

    /// <summary>
    /// Creates a user-made group from <paramref name="fileIds"/> in order, taking them out of their groups, in one
    /// transaction. The caller has checked the ids are movable. Returns the new group's id.
    /// </summary>
    public long CreateGroup(string? title, string? author, IReadOnlyList<long> fileIds, long scanRunId)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var groupId = connection.ExecuteScalar<long>(
            """
            INSERT INTO FileGroup (Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId, UserCorrected)
            VALUES (@title, @author, 'High', @evidence, 0, 0, @scanRunId, 1)
            RETURNING Id
            """,
            new { title, author, evidence = JsonSerializer.Serialize(new[] { "Grouped by the user" }), scanRunId },
            transaction);
        PlaceFiles(connection, transaction, groupId, fileIds, 0);
        transaction.Commit();
        return groupId;
    }

    /// <summary>
    /// Takes the files out of their groups and inserts them into <paramref name="targetGroupId"/> after
    /// <paramref name="afterPosition"/>, keeping their disc and track (an
    /// unresolved file's come from its tags). Then renumbers the source groups, deletes the
    /// emptied ones, and recomputes totals for the target and surviving sources, marking them all user-corrected.
    /// </summary>
    private static void PlaceFiles(
        SqliteConnection connection, SqliteTransaction transaction, long targetGroupId, IReadOnlyList<long> fileIds, int afterPosition)
    {
        var rows = connection.Query<(long FileId, long? GroupId, int? Disc, int? Track)>(
                """
                SELECT a.Id, gf.GroupId,
                    CASE WHEN gf.GroupId IS NULL THEN a.Disc ELSE gf.Disc END,
                    CASE WHEN gf.GroupId IS NULL THEN a.Track ELSE gf.Track END
                FROM AudioFile a LEFT JOIN FileGroupFile gf ON gf.AudioFileId = a.Id
                WHERE a.Id IN @fileIds
                """,
                new { fileIds }, transaction)
            .ToDictionary(r => r.FileId);
        var sources = rows.Values.Where(r => r.GroupId is not null).Select(r => r.GroupId!.Value).Distinct().ToList();

        connection.Execute("DELETE FROM FileGroupFile WHERE AudioFileId IN @fileIds", new { fileIds }, transaction);
        connection.Execute(
            """
            INSERT INTO FileGroupFile (GroupId, AudioFileId, Position, Disc, Track)
            VALUES (@GroupId, @AudioFileId, @Position, @Disc, @Track)
            """,
            fileIds.Select((id, i) => new
            {
                GroupId = targetGroupId, AudioFileId = id, Position = afterPosition + i + 1, rows[id].Disc, rows[id].Track,
            }),
            transaction);

        foreach (var source in sources.Where(s => s != targetGroupId))
        {
            var remaining = connection.Query<long>(
                "SELECT AudioFileId FROM FileGroupFile WHERE GroupId = @source ORDER BY Position, AudioFileId",
                new { source }, transaction).ToList();
            if (remaining.Count == 0)
            {
                connection.Execute("DELETE FROM FileGroup WHERE Id = @source", new { source }, transaction);
                continue;
            }
            connection.Execute(
                "UPDATE FileGroupFile SET Position = @Position WHERE AudioFileId = @FileId",
                remaining.Select((fileId, i) => new { FileId = fileId, Position = i + 1 }),
                transaction);
        }

        connection.Execute(
            """
            UPDATE FileGroup SET UserCorrected = 1,
                FileCount = (SELECT COUNT(*) FROM FileGroupFile gf WHERE gf.GroupId = FileGroup.Id),
                TotalDurationMs = (SELECT COALESCE(SUM(COALESCE(a.DurationMs, 0)), 0)
                                   FROM FileGroupFile gf JOIN AudioFile a ON a.Id = gf.AudioFileId
                                   WHERE gf.GroupId = FileGroup.Id)
            WHERE Id IN @ids
            """,
            new { ids = sources.Append(targetGroupId).Distinct().ToList() }, transaction);
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
