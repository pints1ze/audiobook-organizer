using System.Text.RegularExpressions;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Saved is set on success; NotFound when the person to update doesn't exist; otherwise Errors.</summary>
public record PersonSaveResult(PersonDto? Saved, IReadOnlyDictionary<string, string[]> Errors, bool NotFound = false);

public partial class PersonService(PersonRepository repository)
{
    // Error keys match the camelCase JSON field names the client sends.
    public const string NameField = "name";
    public const string AliasesField = "aliases";

    /// <summary>Display text: trimmed, with each run of whitespace collapsed to one space.</summary>
    public static string Clean(string name) => Whitespace().Replace(name.Trim(), " ");

    /// <summary>
    /// The match key for a name: cleaned, a single-comma "Last, First" read as "First Last", then upper-cased.
    /// Names with two or more commas ("King, Stephen, Jr.") aren't swapped.
    /// </summary>
    public static string NameKey(string name)
    {
        var cleaned = Clean(name);
        var parts = cleaned.Split(',');
        if (parts.Length == 2) cleaned = Clean($"{parts[1]} {parts[0]}");
        return cleaned.ToUpperInvariant();
    }

    public IReadOnlyList<PersonDto> List() => repository.List();

    public PersonDto? Resolve(string name) => repository.FindByKey(NameKey(name));

    public PersonSaveResult Create(PersonRequest request) => Save(null, request);

    public PersonSaveResult Update(long id, PersonRequest request)
    {
        if (repository.Get(id) is null) return new PersonSaveResult(null, new Dictionary<string, string[]>(), NotFound: true);
        return Save(id, request);
    }

    private PersonSaveResult Save(long? id, PersonRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var aliasInput = request.Aliases ?? [];

        if (string.IsNullOrWhiteSpace(request.Name)) errors[NameField] = ["Enter a name."];
        if (aliasInput.Any(string.IsNullOrWhiteSpace)) errors[AliasesField] = ["Aliases can't be blank."];
        if (errors.Count > 0) return new PersonSaveResult(null, errors);

        var name = Clean(request.Name!);
        var nameKey = NameKey(name);

        // Variants of the name itself, or repeats of an earlier alias, add nothing; drop them rather than reject.
        var seen = new HashSet<string> { nameKey };
        var aliases = aliasInput
            .Select(a => new PersonAliasRow(Clean(a), NameKey(a)))
            .Where(a => seen.Add(a.AliasKey))
            .ToList();

        var nameOwner = repository.FindByKey(nameKey);
        if (nameOwner is not null && nameOwner.Id != id) errors[NameField] = [$"Already used by {nameOwner.Name}."];

        var aliasClashes = aliases
            .Select(a => (a.Alias, Owner: repository.FindByKey(a.AliasKey)))
            .Where(c => c.Owner is not null && c.Owner.Id != id)
            .Select(c => $"{c.Alias}: Already used by {c.Owner!.Name}.")
            .ToArray();
        if (aliasClashes.Length > 0) errors[AliasesField] = aliasClashes;

        if (errors.Count > 0) return new PersonSaveResult(null, errors);

        var saved = id is null
            ? repository.Create(name, nameKey, aliases)
            : repository.Update(id.Value, name, nameKey, aliases);
        return saved is null
            ? new PersonSaveResult(null, errors, NotFound: true)
            : new PersonSaveResult(saved, errors);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
