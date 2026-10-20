namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A canonical person or genre with its aliases.</summary>
public record CanonicalEntryDto(int Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>Create or replace a person or genre. Null aliases mean none.</summary>
public record CanonicalEntryRequest(string? Name, IReadOnlyList<string>? Aliases);

/// <summary>Merge the entry <c>FromId</c> into the entry named in the URL.</summary>
public record MergeRequest(long? FromId);

/// <summary>Two entries that are probably the same: merging <c>Merge</c> into <c>Keep</c> is only ever suggested.</summary>
public record MergeSuggestionDto(CanonicalEntryDto Keep, CanonicalEntryDto Merge, string Reason);

/// <summary>A person or genre a name probably refers to, with why. Linking is only ever suggested.</summary>
public record NameSuggestionDto(CanonicalEntryDto Entry, string Reason);

/// <summary>One stored name on a book: the entry it resolves to through the alias catalog, or likely entries.</summary>
public record NameLinkDto(string Name, CanonicalEntryDto? Entry, IReadOnlyList<NameSuggestionDto> Suggestions);

/// <summary>A book's authors and narrators, each with its canonical person or suggestions.</summary>
public record BookPeopleDto(long GroupId, IReadOnlyList<NameLinkDto> Authors, IReadOnlyList<NameLinkDto> Narrators);

/// <summary>A book's genres in stored order, each with its canonical genre or suggestions.</summary>
public record BookGenresDto(long GroupId, IReadOnlyList<NameLinkDto> Genres);
