using AudiobookOrganizer.Api.Models.Local;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class CandidateRepository(SqliteConnectionFactory connections)
{
    /// <summary>
    /// In one transaction: replaces the group's search and candidates with these (their GroupId is ignored; Rank is
    /// their order). The caller has checked the group exists.
    /// </summary>
    public void Replace(long groupId, string searchedAtUtc, string errorsJson, IReadOnlyList<MetadataCandidate> candidates)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        // Cascades to MetadataCandidate.
        connection.Execute("DELETE FROM CandidateSearch WHERE GroupId = @groupId", new { groupId }, transaction);
        connection.Execute(
            "INSERT INTO CandidateSearch (GroupId, SearchedAtUtc, ErrorsJson) VALUES (@groupId, @searchedAtUtc, @errorsJson)",
            new { groupId, searchedAtUtc, errorsJson }, transaction);
        connection.Execute(
            """
            INSERT INTO MetadataCandidate (GroupId, Rank, Provider, CandidateJson, Score)
            VALUES (@GroupId, @Rank, @Provider, @CandidateJson, @Score)
            """,
            candidates.Select((c, rank) => new { GroupId = groupId, Rank = rank, c.Provider, c.CandidateJson, c.Score }),
            transaction);
        transaction.Commit();
    }

    /// <summary>
    /// Ids of the groups never searched that have no chosen match and no processing job of any status, by id.
    /// </summary>
    public IReadOnlyList<long> GetUnsearchedGroupIds()
    {
        using var connection = connections.Open();
        return connection.Query<long>(
            """
            SELECT g.Id FROM FileGroup g
            WHERE NOT EXISTS (SELECT 1 FROM CandidateSearch s WHERE s.GroupId = g.Id)
              AND NOT EXISTS (SELECT 1 FROM Audiobook a JOIN MetadataMatch m ON m.AudiobookId = a.Id WHERE a.GroupId = g.Id)
              AND NOT EXISTS (SELECT 1 FROM ProcessingJob j WHERE j.GroupId = g.Id)
            ORDER BY g.Id
            """).ToList();
    }

    /// <summary>Each searched group's best (rank 0) candidate score, by group id; groups with no candidate are absent.</summary>
    public IReadOnlyDictionary<long, int> GetBestScores()
    {
        using var connection = connections.Open();
        return connection.Query<(long GroupId, int Score)>("SELECT GroupId, Score FROM MetadataCandidate WHERE Rank = 0")
            .ToDictionary(r => r.GroupId, r => r.Score);
    }

    /// <summary>The group's last search and its candidates by rank; null when the group was never searched.</summary>
    public (CandidateSearch Search, IReadOnlyList<MetadataCandidate> Candidates)? Get(long groupId)
    {
        using var connection = connections.Open();
        var search = connection.QuerySingleOrDefault<CandidateSearch>(
            "SELECT GroupId, SearchedAtUtc, ErrorsJson FROM CandidateSearch WHERE GroupId = @groupId", new { groupId });
        if (search is null) return null;
        var candidates = connection.Query<MetadataCandidate>(
            """
            SELECT GroupId, Rank, Provider, CandidateJson, Score FROM MetadataCandidate
            WHERE GroupId = @groupId ORDER BY Rank
            """,
            new { groupId }).ToList();
        return (search, candidates);
    }
}
