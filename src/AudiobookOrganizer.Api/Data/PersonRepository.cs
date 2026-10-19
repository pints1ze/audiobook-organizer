using AudiobookOrganizer.Api.Models.Api;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Data;

public record PersonAliasRow(string Alias, string AliasKey);

public class PersonRepository(SqliteConnectionFactory connections)
{
    private record PersonRow(long Id, string Name);

    private record AliasRow(long PersonId, string Alias);

    public IReadOnlyList<PersonDto> List()
    {
        using var connection = connections.Open();
        var people = connection.Query<PersonRow>("SELECT Id, Name FROM Person ORDER BY Name COLLATE NOCASE, Id");
        var aliases = connection.Query<AliasRow>("SELECT PersonId, Alias FROM PersonAlias ORDER BY Alias COLLATE NOCASE")
            .ToLookup(a => a.PersonId, a => a.Alias);
        return people.Select(p => new PersonDto((int)p.Id, p.Name, aliases[p.Id].ToList())).ToList();
    }

    public PersonDto? Get(long id)
    {
        using var connection = connections.Open();
        return Get(connection, id);
    }

    /// <summary>The person whose name key or one of whose alias keys equals <paramref name="key"/>.</summary>
    public PersonDto? FindByKey(string key)
    {
        using var connection = connections.Open();
        var id = connection.QuerySingleOrDefault<long?>(
            """
            SELECT Id FROM Person WHERE NameKey = @key
            UNION SELECT PersonId FROM PersonAlias WHERE AliasKey = @key
            LIMIT 1
            """, new { key });
        return id is null ? null : Get(connection, id.Value);
    }

    public PersonDto Create(string name, string nameKey, IReadOnlyList<PersonAliasRow> aliases)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var id = connection.ExecuteScalar<long>(
            "INSERT INTO Person (Name, NameKey) VALUES (@name, @nameKey) RETURNING Id",
            new { name, nameKey }, transaction);
        InsertAliases(connection, transaction, id, aliases);
        transaction.Commit();
        return Get(connection, id)!;
    }

    /// <summary>Replaces the name and the whole alias list. Returns null if there's no such person.</summary>
    public PersonDto? Update(long id, string name, string nameKey, IReadOnlyList<PersonAliasRow> aliases)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var changed = connection.Execute(
            "UPDATE Person SET Name = @name, NameKey = @nameKey WHERE Id = @id",
            new { id, name, nameKey }, transaction);
        if (changed == 0) return null;
        connection.Execute("DELETE FROM PersonAlias WHERE PersonId = @id", new { id }, transaction);
        InsertAliases(connection, transaction, id, aliases);
        transaction.Commit();
        return Get(connection, id);
    }

    private static void InsertAliases(SqliteConnection connection, SqliteTransaction transaction, long id,
        IReadOnlyList<PersonAliasRow> aliases)
    {
        foreach (var alias in aliases)
        {
            connection.Execute(
                "INSERT INTO PersonAlias (PersonId, Alias, AliasKey) VALUES (@id, @Alias, @AliasKey)",
                new { id, alias.Alias, alias.AliasKey }, transaction);
        }
    }

    private static PersonDto? Get(SqliteConnection connection, long id)
    {
        var person = connection.QuerySingleOrDefault<PersonRow>("SELECT Id, Name FROM Person WHERE Id = @id", new { id });
        if (person is null) return null;
        var aliases = connection.Query<string>(
            "SELECT Alias FROM PersonAlias WHERE PersonId = @id ORDER BY Alias COLLATE NOCASE", new { id }).ToList();
        return new PersonDto((int)person.Id, person.Name, aliases);
    }
}
