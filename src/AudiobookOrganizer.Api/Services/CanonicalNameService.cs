using System.Text.RegularExpressions;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Saved is set on success; NotFound when the entry to update doesn't exist; otherwise Errors.</summary>
public record CanonicalSaveResult(CanonicalEntryDto? Saved, IReadOnlyDictionary<string, string[]> Errors,
    bool NotFound = false);

/// <summary>Canonical people and genres: create, edit and resolve variants by match key, one namespace per kind.</summary>
public partial class CanonicalNameService(CanonicalNameRepository repository)
{
    // Error keys match the camelCase JSON field names the client sends.
    public const string NameField = "name";
    public const string AliasesField = "aliases";
    public const string FromIdField = "fromId";

    /// <summary>Display text: trimmed, with each run of whitespace collapsed to one space.</summary>
    public static string Clean(string name) => Whitespace().Replace(name.Trim(), " ");

    /// <summary>
    /// The match key for a person's name: cleaned, a single-comma "Last, First" read as "First Last", then upper-cased.
    /// Names with two or more commas ("King, Stephen, Jr.") aren't swapped.
    /// </summary>
    public static string NameKey(string name)
    {
        var cleaned = Clean(name);
        var parts = cleaned.Split(',');
        if (parts.Length == 2) cleaned = Clean($"{parts[1]} {parts[0]}");
        return cleaned.ToUpperInvariant();
    }

    /// <summary>The match key for a genre: cleaned, with "-", "_" and "/" read as spaces, then upper-cased. No swap.</summary>
    public static string GenreKey(string genre) =>
        Clean(GenreSeparators().Replace(Clean(genre), " ")).ToUpperInvariant();

    private static string KeyFor(CanonicalKind kind, string text) =>
        kind == CanonicalKind.Genre ? GenreKey(text) : NameKey(text);

    public IReadOnlyList<CanonicalEntryDto> List(CanonicalKind kind) => repository.List(kind);

    /// <summary>Pairs that are probably the same entry. Read-only: never merges.</summary>
    public IReadOnlyList<MergeSuggestionDto> Suggestions(CanonicalKind kind) =>
        CanonicalSuggestions.Suggest(kind, repository.List(kind));

    public CanonicalEntryDto? Resolve(CanonicalKind kind, string text) => repository.FindByKey(kind, KeyFor(kind, text));

    public CanonicalSaveResult Create(CanonicalKind kind, CanonicalEntryRequest request) => Save(kind, null, request);

    public CanonicalSaveResult Update(CanonicalKind kind, long id, CanonicalEntryRequest request)
    {
        if (repository.Get(kind, id) is null)
            return new CanonicalSaveResult(null, new Dictionary<string, string[]>(), NotFound: true);
        return Save(kind, id, request);
    }

    /// <summary>Folds entry <paramref name="fromId"/> into entry <paramref name="id"/>. Only ever the user's explicit action.</summary>
    public CanonicalSaveResult Merge(CanonicalKind kind, long id, long? fromId)
    {
        if (fromId is null || fromId == id)
            return new CanonicalSaveResult(null,
                new Dictionary<string, string[]> { [FromIdField] = ["Choose another entry to merge in."] });
        var merged = repository.Merge(kind, id, fromId.Value);
        return merged is null
            ? new CanonicalSaveResult(null, new Dictionary<string, string[]>(), NotFound: true)
            : new CanonicalSaveResult(merged, new Dictionary<string, string[]>());
    }

    private CanonicalSaveResult Save(CanonicalKind kind, long? id, CanonicalEntryRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var aliasInput = request.Aliases ?? [];

        if (string.IsNullOrWhiteSpace(request.Name)) errors[NameField] = ["Enter a name."];
        if (aliasInput.Any(string.IsNullOrWhiteSpace)) errors[AliasesField] = ["Aliases can't be blank."];
        if (errors.Count > 0) return new CanonicalSaveResult(null, errors);

        var name = Clean(request.Name!);
        var nameKey = KeyFor(kind, name);

        // Variants of the name itself, or repeats of an earlier alias, add nothing; drop them rather than reject.
        var seen = new HashSet<string> { nameKey };
        var aliases = aliasInput
            .Select(a => new AliasRow(Clean(a), KeyFor(kind, a)))
            .Where(a => seen.Add(a.AliasKey))
            .ToList();

        var nameOwner = repository.FindByKey(kind, nameKey);
        if (nameOwner is not null && nameOwner.Id != id) errors[NameField] = [$"Already used by {nameOwner.Name}."];

        var aliasClashes = aliases
            .Select(a => (a.Alias, Owner: repository.FindByKey(kind, a.AliasKey)))
            .Where(c => c.Owner is not null && c.Owner.Id != id)
            .Select(c => $"{c.Alias}: Already used by {c.Owner!.Name}.")
            .ToArray();
        if (aliasClashes.Length > 0) errors[AliasesField] = aliasClashes;

        if (errors.Count > 0) return new CanonicalSaveResult(null, errors);

        var saved = id is null
            ? repository.Create(kind, name, nameKey, aliases)
            : repository.Update(kind, id.Value, name, nameKey, aliases);
        return saved is null
            ? new CanonicalSaveResult(null, errors, NotFound: true)
            : new CanonicalSaveResult(saved, errors);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[-_/]")]
    private static partial Regex GenreSeparators();
}
