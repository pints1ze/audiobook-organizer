using System.Data;
using AudiobookOrganizer.Api.Models.Local;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class AudiobookRepository(SqliteConnectionFactory connections)
{
    /// <summary>The group's current match; null when the group has no Audiobook or no match.</summary>
    public MetadataMatch? GetMatch(long groupId)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<MetadataMatch>(
            """
            SELECT m.Id, m.AudiobookId, m.Provider, m.ProviderId, m.CandidateJson, m.PickedAtUtc
            FROM MetadataMatch m JOIN Audiobook b ON b.Id = m.AudiobookId
            WHERE b.GroupId = @groupId
            """,
            new { groupId });
    }

    /// <summary>The group's stored metadata values; empty when the group has no Audiobook.</summary>
    public IReadOnlyList<AudiobookField> GetFields(long groupId)
    {
        using var connection = connections.Open();
        return connection.Query<AudiobookField>(
            """
            SELECT f.AudiobookId, f.Field, f.ValueJson, f.Origin, f.Provider
            FROM AudiobookField f JOIN Audiobook b ON b.Id = f.AudiobookId
            WHERE b.GroupId = @groupId
            """,
            new { groupId }).ToList();
    }

    /// <summary>
    /// In one transaction: creates the group's Audiobook if missing (else bumps its UpdatedAtUtc), upserts
    /// <paramref name="fields"/> (their AudiobookId is ignored), and marks the group user-corrected. The caller has
    /// checked the group exists.
    /// </summary>
    public void SaveFields(long groupId, IEnumerable<AudiobookField> fields, string nowUtc)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var audiobookId = UpsertAudiobook(connection, transaction, groupId, nowUtc);
        UpsertFields(connection, transaction, audiobookId, fields);
        MarkCorrected(connection, transaction, groupId);
        transaction.Commit();
    }

    /// <summary>
    /// In one transaction: creates the group's Audiobook if missing (else bumps its UpdatedAtUtc), replaces its match,
    /// upserts <paramref name="fields"/>, and marks the group user-corrected so rescans keep it. The caller has checked
    /// the group exists.
    /// </summary>
    public void SaveMatch(long groupId, string provider, string? providerId, string candidateJson,
        IEnumerable<AudiobookField> fields, string nowUtc)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var audiobookId = UpsertAudiobook(connection, transaction, groupId, nowUtc);
        connection.Execute("DELETE FROM MetadataMatch WHERE AudiobookId = @audiobookId", new { audiobookId }, transaction);
        connection.Execute(
            """
            INSERT INTO MetadataMatch (AudiobookId, Provider, ProviderId, CandidateJson, PickedAtUtc)
            VALUES (@audiobookId, @provider, @providerId, @candidateJson, @nowUtc)
            """,
            new { audiobookId, provider, providerId, candidateJson, nowUtc }, transaction);
        UpsertFields(connection, transaction, audiobookId, fields);
        MarkCorrected(connection, transaction, groupId);
        transaction.Commit();
    }

    /// <summary>The group's chosen cover without its image; null when there's none.</summary>
    public AudiobookCover? GetCover(long groupId)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<AudiobookCover>(
            """
            SELECT c.AudiobookId, c.Source, c.FileId, c.MimeType, c.ChosenAtUtc
            FROM AudiobookCover c JOIN Audiobook b ON b.Id = c.AudiobookId
            WHERE b.GroupId = @groupId
            """,
            new { groupId });
    }

    /// <summary>The group's chosen cover with its image; null when there's none.</summary>
    public AudiobookCover? GetCoverImage(long groupId)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<AudiobookCover>(
            """
            SELECT c.AudiobookId, c.Source, c.FileId, c.MimeType, c.Image, c.ChosenAtUtc
            FROM AudiobookCover c JOIN Audiobook b ON b.Id = c.AudiobookId
            WHERE b.GroupId = @groupId
            """,
            new { groupId });
    }

    /// <summary>
    /// In one transaction: creates the group's Audiobook if missing (else bumps its UpdatedAtUtc), replaces its chosen
    /// cover with <paramref name="cover"/> (its AudiobookId and ChosenAtUtc are ignored), and marks the group
    /// user-corrected. The caller has checked the group exists.
    /// </summary>
    public void SaveCover(long groupId, AudiobookCover cover, string nowUtc)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var audiobookId = UpsertAudiobook(connection, transaction, groupId, nowUtc);
        connection.Execute(
            """
            INSERT INTO AudiobookCover (AudiobookId, Source, FileId, MimeType, Image, ChosenAtUtc)
            VALUES (@audiobookId, @Source, @FileId, @MimeType, @Image, @nowUtc)
            ON CONFLICT (AudiobookId) DO UPDATE
            SET Source = excluded.Source, FileId = excluded.FileId, MimeType = excluded.MimeType,
                Image = excluded.Image, ChosenAtUtc = excluded.ChosenAtUtc
            """,
            new { audiobookId, cover.Source, cover.FileId, cover.MimeType, cover.Image, nowUtc }, transaction);
        MarkCorrected(connection, transaction, groupId);
        transaction.Commit();
    }

    /// <summary>The group's files whose scan found an embedded cover, in position order.</summary>
    public IReadOnlyList<GroupFileRow> GetCoverFiles(long groupId)
    {
        using var connection = connections.Open();
        return connection.Query<GroupFileRow>(
            """
            SELECT gf.AudioFileId AS FileId, a.SourcePath, gf.Position, gf.Disc, gf.Track, a.DurationMs
            FROM FileGroupFile gf JOIN AudioFile a ON a.Id = gf.AudioFileId
            WHERE gf.GroupId = @groupId AND a.HasCover = 1
            ORDER BY gf.Position, gf.AudioFileId
            """,
            new { groupId }).ToList();
    }

    private static long UpsertAudiobook(IDbConnection connection, IDbTransaction transaction, long groupId, string nowUtc) =>
        connection.ExecuteScalar<long>(
            """
            INSERT INTO Audiobook (GroupId, CreatedAtUtc, UpdatedAtUtc) VALUES (@groupId, @nowUtc, @nowUtc)
            ON CONFLICT (GroupId) DO UPDATE SET UpdatedAtUtc = excluded.UpdatedAtUtc
            RETURNING Id
            """,
            new { groupId, nowUtc }, transaction);

    private static void UpsertFields(IDbConnection connection, IDbTransaction transaction, long audiobookId,
        IEnumerable<AudiobookField> fields) =>
        connection.Execute(
            """
            INSERT INTO AudiobookField (AudiobookId, Field, ValueJson, Origin, Provider)
            VALUES (@AudiobookId, @Field, @ValueJson, @Origin, @Provider)
            ON CONFLICT (AudiobookId, Field) DO UPDATE
            SET ValueJson = excluded.ValueJson, Origin = excluded.Origin, Provider = excluded.Provider
            """,
            fields.Select(f => new { AudiobookId = audiobookId, f.Field, f.ValueJson, f.Origin, f.Provider }),
            transaction);

    private static void MarkCorrected(IDbConnection connection, IDbTransaction transaction, long groupId) =>
        connection.Execute("UPDATE FileGroup SET UserCorrected = 1 WHERE Id = @groupId", new { groupId }, transaction);
}
