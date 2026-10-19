namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A series and the book's place in it. Part stays a string ("1", "1.5" or empty) until the editor parses it.</summary>
public record SeriesRefDto(string Name, string? Part);

/// <summary>A book a metadata provider proposes. Provider is e.g. "audible"; Format is the provider's value, e.g. "unabridged".</summary>
public record MetadataCandidateDto(
    string Provider,
    string? Asin,
    string? Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Narrators,
    SeriesRefDto? Series,
    string? Publisher,
    string? ReleaseDate,
    string? Language,
    int? RuntimeMinutes,
    string? Format,
    string? CoverUrl);
