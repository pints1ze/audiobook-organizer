namespace AudiobookOrganizer.Api.Models.Api;

public record PersonDto(int Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>Create or replace a person. Null aliases mean none.</summary>
public record PersonRequest(string? Name, IReadOnlyList<string>? Aliases);
