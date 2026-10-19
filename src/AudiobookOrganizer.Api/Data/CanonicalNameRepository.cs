using AudiobookOrganizer.Api.Models.Api;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Data;

public enum CanonicalKind { Person, Genre }

public record AliasRow(string Alias, string AliasKey);

/// <summary>Canonical people and genres with their aliases. Each kind has its own pair of tables.</summary>
public class CanonicalNameRepository(SqliteConnectionFactory connections)
{
    private record Tables(string Entry, string Alias, string ForeignKey);

    // Table and column names are only ever taken from this map, never from input, so the interpolated SQL is safe.
    private static readonly IReadOnlyDictionary<CanonicalKind, Tables> TableMap = new Dictionary<CanonicalKind, Tables>
    {
        [CanonicalKind.Person] = new("Person", "PersonAlias", "PersonId"),
        [CanonicalKind.Genre] = new("Genre", "GenreAlias", "GenreId"),
    };

    private record EntryRow(long Id, string Name);

    private record EntryAliasRow(long EntryId, string Alias);

    public IReadOnlyList<CanonicalEntryDto> List(CanonicalKind kind)
    {
        var t = TableMap[kind];
        using var connection = connections.Open();
        var entries = connection.Query<EntryRow>($"SELECT Id, Name FROM {t.Entry} ORDER BY Name COLLATE NOCASE, Id");
        var aliases = connection.Query<EntryAliasRow>(
                $"SELECT {t.ForeignKey} AS EntryId, Alias FROM {t.Alias} ORDER BY Alias COLLATE NOCASE")
            .ToLookup(a => a.EntryId, a => a.Alias);
        return entries.Select(e => new CanonicalEntryDto((int)e.Id, e.Name, aliases[e.Id].ToList())).ToList();
    }

    public CanonicalEntryDto? Get(CanonicalKind kind, long id)
    {
        using var connection = connections.Open();
        return Get(connection, TableMap[kind], id);
    }

    /// <summary>The entry whose name key or one of whose alias keys equals <paramref name="key"/>.</summary>
    public CanonicalEntryDto? FindByKey(CanonicalKind kind, string key)
    {
        var t = TableMap[kind];
        using var connection = connections.Open();
        var id = connection.QuerySingleOrDefault<long?>(
            $"""
            SELECT Id FROM {t.Entry} WHERE NameKey = @key
            UNION SELECT {t.ForeignKey} FROM {t.Alias} WHERE AliasKey = @key
            LIMIT 1
            """, new { key });
        return id is null ? null : Get(connection, t, id.Value);
    }

    public CanonicalEntryDto Create(CanonicalKind kind, string name, string nameKey, IReadOnlyList<AliasRow> aliases)
    {
        var t = TableMap[kind];
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var id = connection.ExecuteScalar<long>(
            $"INSERT INTO {t.Entry} (Name, NameKey) VALUES (@name, @nameKey) RETURNING Id",
            new { name, nameKey }, transaction);
        InsertAliases(connection, transaction, t, id, aliases);
        transaction.Commit();
        return Get(connection, t, id)!;
    }

    /// <summary>Replaces the name and the whole alias list. Returns null if there's no such entry.</summary>
    public CanonicalEntryDto? Update(CanonicalKind kind, long id, string name, string nameKey,
        IReadOnlyList<AliasRow> aliases)
    {
        var t = TableMap[kind];
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var changed = connection.Execute(
            $"UPDATE {t.Entry} SET Name = @name, NameKey = @nameKey WHERE Id = @id",
            new { id, name, nameKey }, transaction);
        if (changed == 0) return null;
        connection.Execute($"DELETE FROM {t.Alias} WHERE {t.ForeignKey} = @id", new { id }, transaction);
        InsertAliases(connection, transaction, t, id, aliases);
        transaction.Commit();
        return Get(connection, t, id);
    }

    /// <summary>
    /// Folds entry <paramref name="fromId"/> into <paramref name="intoId"/> in one transaction: its aliases move over,
    /// its name becomes an alias and the entry is deleted. Returns null, changing nothing, if either entry is missing.
    /// </summary>
    public CanonicalEntryDto? Merge(CanonicalKind kind, long intoId, long fromId)
    {
        var t = TableMap[kind];
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var into = connection.QuerySingleOrDefault<long?>($"SELECT Id FROM {t.Entry} WHERE Id = @intoId",
            new { intoId }, transaction);
        var from = connection.QuerySingleOrDefault<(string Name, string NameKey)?>(
            $"SELECT Name, NameKey FROM {t.Entry} WHERE Id = @fromId", new { fromId }, transaction);
        if (into is null || from is null) return null;
        connection.Execute($"UPDATE {t.Alias} SET {t.ForeignKey} = @intoId WHERE {t.ForeignKey} = @fromId",
            new { intoId, fromId }, transaction);
        // Delete before inserting the old name as an alias, so its key is never held twice at once.
        connection.Execute($"DELETE FROM {t.Entry} WHERE Id = @fromId", new { fromId }, transaction);
        InsertAliases(connection, transaction, t, intoId, [new AliasRow(from.Value.Name, from.Value.NameKey)]);
        transaction.Commit();
        return Get(connection, t, intoId);
    }

    private static void InsertAliases(SqliteConnection connection, SqliteTransaction transaction, Tables t, long id,
        IReadOnlyList<AliasRow> aliases)
    {
        foreach (var alias in aliases)
        {
            connection.Execute(
                $"INSERT INTO {t.Alias} ({t.ForeignKey}, Alias, AliasKey) VALUES (@id, @Alias, @AliasKey)",
                new { id, alias.Alias, alias.AliasKey }, transaction);
        }
    }

    private static CanonicalEntryDto? Get(SqliteConnection connection, Tables t, long id)
    {
        var entry = connection.QuerySingleOrDefault<EntryRow>($"SELECT Id, Name FROM {t.Entry} WHERE Id = @id", new { id });
        if (entry is null) return null;
        var aliases = connection.Query<string>(
            $"SELECT Alias FROM {t.Alias} WHERE {t.ForeignKey} = @id ORDER BY Alias COLLATE NOCASE", new { id }).ToList();
        return new CanonicalEntryDto((int)entry.Id, entry.Name, aliases);
    }
}
