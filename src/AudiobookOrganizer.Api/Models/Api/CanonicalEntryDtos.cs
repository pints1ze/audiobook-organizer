namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A canonical person or genre with its aliases.</summary>
public record CanonicalEntryDto(int Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>Create or replace a person or genre. Null aliases mean none.</summary>
public record CanonicalEntryRequest(string? Name, IReadOnlyList<string>? Aliases);

/// <summary>Merge the entry <c>FromId</c> into the entry named in the URL.</summary>
public record MergeRequest(long? FromId);
