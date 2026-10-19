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
    string? CoverUrl,
    string? Isbn = null);

/// <summary>The candidate the user picked for a book (a group), and when.</summary>
public record BookMatchDto(long GroupId, MetadataCandidateDto Match, DateTimeOffset PickedAtUtc);

/// <summary>Candidates from the first provider that found any, plus the errors of providers that failed before it.</summary>
public record MetadataSearchResultDto(IReadOnlyList<MetadataCandidateDto> Candidates, IReadOnlyList<string> ProviderErrors);

/// <summary>One metadata value and where it came from: origin "file", "provider", "user" or null; Provider only for "provider".</summary>
public record MetadataFieldDto<T>(T Value, string? Origin, string? Provider);

/// <summary>A book's metadata values, one entry per field.</summary>
public record BookMetadataFieldsDto(
    MetadataFieldDto<string?> Title,
    MetadataFieldDto<string?> Subtitle,
    MetadataFieldDto<IReadOnlyList<string>> Authors,
    MetadataFieldDto<IReadOnlyList<string>> Narrators,
    MetadataFieldDto<string?> Series,
    MetadataFieldDto<string?> SeriesPart,
    MetadataFieldDto<string?> Publisher,
    MetadataFieldDto<string?> ReleaseDate,
    MetadataFieldDto<string?> Language,
    MetadataFieldDto<string?> Format,
    MetadataFieldDto<string?> Asin,
    MetadataFieldDto<string?> Isbn);

/// <summary>A book's (a group's) metadata values.</summary>
public record BookMetadataDto(long GroupId, BookMetadataFieldsDto Fields);

/// <summary>The values a user sets; a null (or missing) property leaves that field alone.</summary>
public record UpdateBookMetadataFieldsDto(
    string? Title = null,
    string? Subtitle = null,
    IReadOnlyList<string>? Authors = null,
    IReadOnlyList<string>? Narrators = null,
    string? Series = null,
    string? SeriesPart = null,
    string? Publisher = null,
    string? ReleaseDate = null,
    string? Language = null,
    string? Format = null,
    string? Asin = null,
    string? Isbn = null);

/// <summary>The body of PUT /api/groups/{id}/metadata: <c>{ fields: { name: value, ... } }</c>.</summary>
public record UpdateBookMetadataDto(UpdateBookMetadataFieldsDto Fields);

/// <summary>A book's chosen cover. Source is "embedded" (FileId is the file it was read from).</summary>
public record ChosenCoverDto(string Source, long? FileId, string MimeType, DateTimeOffset ChosenAtUtc);

/// <summary>A file of the book whose scan found an embedded cover.</summary>
public record EmbeddedCoverDto(long FileId, string RelativePath);

/// <summary>A book's (a group's) chosen cover, if any, and the embedded covers it can choose from, in file order.</summary>
public record BookCoverDto(long GroupId, ChosenCoverDto? Chosen, IReadOnlyList<EmbeddedCoverDto> Embedded);

/// <summary>The body of PUT /api/groups/{id}/cover: <c>{ source: "embedded", fileId }</c>.</summary>
public record ChooseCoverDto(string? Source, long? FileId);
