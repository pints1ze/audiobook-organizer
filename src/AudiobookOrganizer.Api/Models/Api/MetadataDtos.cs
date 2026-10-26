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

/// <summary>A stored candidate and its score (0–100) against the book's title, authors and runtime.</summary>
public record ScoredCandidateDto(MetadataCandidateDto Candidate, int Score);

/// <summary>A book's last catalog search: its top candidates, best first, and the provider errors.</summary>
public record BookCandidatesDto(long GroupId, DateTimeOffset SearchedAtUtc, IReadOnlyList<ScoredCandidateDto> Candidates,
    IReadOnlyList<string> Errors);

/// <summary>One metadata value and where it came from: origin "file", "provider", "user" or null; Provider only for "provider".</summary>
public record MetadataFieldDto<T>(T Value, string? Origin, string? Provider);

/// <summary>A book's metadata values, one entry per field.</summary>
public record BookMetadataFieldsDto(
    MetadataFieldDto<string?> Title,
    MetadataFieldDto<string?> Subtitle,
    MetadataFieldDto<IReadOnlyList<string>> Authors,
    MetadataFieldDto<IReadOnlyList<string>> Narrators,
    MetadataFieldDto<IReadOnlyList<string>> Genres,
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
    IReadOnlyList<string>? Genres = null,
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

/// <summary>A book's chosen cover. Source is "embedded" (FileId is the file it was read from) or "provider" (FileId null).</summary>
public record ChosenCoverDto(string Source, long? FileId, string MimeType, DateTimeOffset ChosenAtUtc);

/// <summary>A file of the book whose scan found an embedded cover.</summary>
public record EmbeddedCoverDto(long FileId, string RelativePath);

/// <summary>
/// The current match's cover, which PUT .../cover with source "provider" downloads. IsChosen is true when the chosen
/// cover was downloaded from this match (chosen at or after the match was picked).
/// </summary>
public record ProviderCoverDto(string Provider, string CoverUrl, bool IsChosen);

/// <summary>
/// A book's (a group's) chosen cover, if any, and the covers it can choose from: the embedded ones in file order and
/// the current match's (null when there's no match or it has no cover URL).
/// </summary>
public record BookCoverDto(long GroupId, ChosenCoverDto? Chosen, IReadOnlyList<EmbeddedCoverDto> Embedded,
    ProviderCoverDto? Provider);

/// <summary>The body of PUT /api/groups/{id}/cover: <c>{ source: "embedded", fileId }</c> or <c>{ source: "provider" }</c>.</summary>
public record ChooseCoverDto(string? Source, long? FileId);
