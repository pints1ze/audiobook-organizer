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

    /// <summary>
    /// In one transaction: creates the group's Audiobook if missing (else bumps its UpdatedAtUtc), replaces its match,
    /// and marks the group user-corrected so rescans keep it. The caller has checked the group exists.
    /// </summary>
    public void SaveMatch(long groupId, string provider, string? providerId, string candidateJson, string nowUtc)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var audiobookId = connection.ExecuteScalar<long>(
            """
            INSERT INTO Audiobook (GroupId, CreatedAtUtc, UpdatedAtUtc) VALUES (@groupId, @nowUtc, @nowUtc)
            ON CONFLICT (GroupId) DO UPDATE SET UpdatedAtUtc = excluded.UpdatedAtUtc
            RETURNING Id
            """,
            new { groupId, nowUtc }, transaction);
        connection.Execute("DELETE FROM MetadataMatch WHERE AudiobookId = @audiobookId", new { audiobookId }, transaction);
        connection.Execute(
            """
            INSERT INTO MetadataMatch (AudiobookId, Provider, ProviderId, CandidateJson, PickedAtUtc)
            VALUES (@audiobookId, @provider, @providerId, @candidateJson, @nowUtc)
            """,
            new { audiobookId, provider, providerId, candidateJson, nowUtc }, transaction);
        connection.Execute("UPDATE FileGroup SET UserCorrected = 1 WHERE Id = @groupId", new { groupId }, transaction);
        transaction.Commit();
    }
}
