using System.Globalization;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;

namespace AudiobookOrganizer.Api.Services;

/// <summary>The stored match, or errors keyed by JSON field name. No match and no errors means the group id is unknown.</summary>
public record PickMatchResult(BookMatchDto? Match, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>A group's GET match outcome: whether the group exists, and its match if it has one.</summary>
public record GetMatchResult(bool GroupFound, BookMatchDto? Match);

/// <summary>A group's metadata after an edit, or errors keyed by JSON field name. Neither means the group id is unknown.</summary>
public record UpdateMetadataResult(BookMetadataDto? Metadata, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>Stores the catalog candidate the user picks for a book, and the book's metadata values with their origins.</summary>
public class AudiobookService(GroupRepository groups, AudiobookRepository audiobooks, CanonicalNameService names)
{
    public const int MaxPeople = 50;

    private static readonly HashSet<string> Providers = ["audible", "openlibrary"];

    public GetMatchResult GetMatch(long groupId)
    {
        if (groups.GetGroup(groupId) is null) return new GetMatchResult(false, null);
        return new GetMatchResult(true, audiobooks.GetMatch(groupId) is { } match ? ToDto(groupId, match) : null);
    }

    /// <summary>
    /// Validates and trims <paramref name="candidate"/>, then stores it as the group's one current match, copies its
    /// values into the book (see <see cref="ValuesToCopy"/>), creating the group's Audiobook if needed and marking the
    /// group user-corrected.
    /// </summary>
    public PickMatchResult PickMatch(long groupId, MetadataCandidateDto candidate)
    {
        if (groups.GetGroup(groupId) is null) return new PickMatchResult(null, new Dictionary<string, string[]>());

        var (clean, errors) = Clean(candidate);
        if (errors.Count > 0) return new PickMatchResult(null, errors);

        audiobooks.SaveMatch(groupId, clean.Provider, clean.Asin ?? clean.Isbn,
            JsonSerializer.Serialize(clean, JsonSerializerOptions.Web), ValuesToCopy(groupId, clean),
            DateTimeOffset.UtcNow.ToString("O"));
        return new PickMatchResult(ToDto(groupId, audiobooks.GetMatch(groupId)!), errors);
    }

    /// <summary>The group's metadata values; null when the group id is unknown.</summary>
    public BookMetadataDto? GetMetadata(long groupId)
    {
        if (groups.GetGroup(groupId) is not { } group) return null;
        var rows = audiobooks.GetFields(groupId).ToDictionary(f => f.Field);

        MetadataFieldDto<string?> Text(string field, string? fallback = null) =>
            rows.TryGetValue(field, out var row)
                ? new(JsonSerializer.Deserialize<string?>(row.ValueJson), row.Origin, row.Provider)
                : fallback is null ? new(null, null, null) : new(fallback, "file", null);

        MetadataFieldDto<IReadOnlyList<string>> People(string field, string? fallback = null) =>
            rows.TryGetValue(field, out var row)
                ? new(JsonSerializer.Deserialize<List<string>>(row.ValueJson) ?? [], row.Origin, row.Provider)
                : fallback is null ? new([], null, null) : new([fallback], "file", null);

        // shortcut: the group's title and author are labelled "file" even when the user renamed the group on Incoming,
        // because the group doesn't record which correction set UserCorrected — revisit if it confuses the editor.
        return new BookMetadataDto(groupId, new BookMetadataFieldsDto(
            Text(Fields.Title, Trim(group.Title)),
            Text(Fields.Subtitle),
            People(Fields.Authors, Trim(group.Author)),
            People(Fields.Narrators),
            People(Fields.Genres),
            Text(Fields.Series),
            Text(Fields.SeriesPart),
            Text(Fields.Publisher),
            Text(Fields.ReleaseDate),
            Text(Fields.Language),
            Text(Fields.Format),
            Text(Fields.Asin),
            Text(Fields.Isbn)));
    }

    /// <summary>
    /// The book's authors and narrators (as <see cref="GetMetadata"/> reads them, file fallback included), each with
    /// the person it resolves to through the alias catalog or, if none, likely people. Null when the group id is
    /// unknown. Computed on each read: nothing is linked or created.
    /// </summary>
    public BookPeopleDto? GetPeople(long groupId)
    {
        if (GetMetadata(groupId) is not { } metadata) return null;
        var link = Linker(CanonicalKind.Person);
        return new BookPeopleDto(groupId,
            metadata.Fields.Authors.Value.Select(link).ToList(),
            metadata.Fields.Narrators.Value.Select(link).ToList());
    }

    /// <summary>
    /// The book's stored genres in order, each with the genre it resolves to through the alias catalog or, if none,
    /// likely genres. Null when the group id is unknown. Computed on each read: nothing is linked or created.
    /// </summary>
    public BookGenresDto? GetGenres(long groupId)
    {
        if (GetMetadata(groupId) is not { } metadata) return null;
        return new BookGenresDto(groupId, metadata.Fields.Genres.Value.Select(Linker(CanonicalKind.Genre)).ToList());
    }

    /// <summary>Resolves a name of <paramref name="kind"/>, or suggests entries; the catalog is loaded once, on first need.</summary>
    private Func<string, NameLinkDto> Linker(CanonicalKind kind)
    {
        IReadOnlyList<CanonicalEntryDto>? catalog = null;
        return name =>
        {
            if (names.Resolve(kind, name) is { } entry) return new NameLinkDto(name, entry, []);
            catalog ??= names.List(kind);
            return new NameLinkDto(name, null, CanonicalSuggestions.ForName(kind, name, catalog));
        };
    }

    /// <summary>
    /// Stores the fields <paramref name="request"/> sends (non-null) as user values, trimmed; blank text and empty
    /// lists are stored as explicit empty user values. Creates the Audiobook if needed and marks the group corrected.
    /// </summary>
    public UpdateMetadataResult UpdateMetadata(long groupId, UpdateBookMetadataDto request)
    {
        if (groups.GetGroup(groupId) is null) return new UpdateMetadataResult(null, new Dictionary<string, string[]>());

        var sent = request.Fields;
        var errors = new Dictionary<string, string[]>();
        var rows = new List<AudiobookField>();

        void SetText(string field, string? value)
        {
            if (value is not null) rows.Add(UserRow(field, JsonSerializer.Serialize(CleanText(errors, field, value))));
        }

        void SetPeople(string field, IReadOnlyList<string>? names, bool entries = false)
        {
            if (names is not null) rows.Add(UserRow(field, JsonSerializer.Serialize(CleanPeople(errors, field, names, entries))));
        }

        SetText(Fields.Title, sent.Title);
        if (sent.Title is not null && Trim(sent.Title) is null) errors[Fields.Title] = ["The title is required."];
        SetText(Fields.Subtitle, sent.Subtitle);
        SetPeople(Fields.Authors, sent.Authors);
        SetPeople(Fields.Narrators, sent.Narrators);
        SetPeople(Fields.Genres, sent.Genres, entries: true);
        SetText(Fields.Series, sent.Series);
        SetText(Fields.SeriesPart, sent.SeriesPart);
        SetText(Fields.Publisher, sent.Publisher);
        SetText(Fields.ReleaseDate, sent.ReleaseDate);
        SetText(Fields.Language, sent.Language);
        SetText(Fields.Format, sent.Format);
        SetText(Fields.Asin, sent.Asin);
        SetText(Fields.Isbn, sent.Isbn);
        if (errors.Count > 0) return new UpdateMetadataResult(null, errors);

        audiobooks.SaveFields(groupId, rows, DateTimeOffset.UtcNow.ToString("O"));
        return new UpdateMetadataResult(GetMetadata(groupId), errors);
    }

    private static AudiobookField UserRow(string field, string valueJson) =>
        new() { Field = field, ValueJson = valueJson, Origin = "user" };

    /// <summary>
    /// The candidate's values to copy into the book: (a) a user value is never changed; (b) a candidate gap (null or
    /// empty list) keeps the current value; (c) an Open Library value never replaces an Audible one.
    /// </summary>
    private List<AudiobookField> ValuesToCopy(long groupId, MetadataCandidateDto clean)
    {
        var current = audiobooks.GetFields(groupId).ToDictionary(f => f.Field);
        (string Field, string? ValueJson)[] candidate =
        [
            (Fields.Title, TextJson(clean.Title)),
            (Fields.Subtitle, TextJson(clean.Subtitle)),
            (Fields.Authors, clean.Authors.Count > 0 ? JsonSerializer.Serialize(clean.Authors) : null),
            (Fields.Narrators, clean.Narrators.Count > 0 ? JsonSerializer.Serialize(clean.Narrators) : null),
            (Fields.Series, TextJson(clean.Series?.Name)),
            (Fields.SeriesPart, TextJson(clean.Series?.Part)),
            (Fields.Publisher, TextJson(clean.Publisher)),
            (Fields.ReleaseDate, TextJson(clean.ReleaseDate)),
            (Fields.Language, TextJson(clean.Language)),
            (Fields.Format, TextJson(clean.Format)),
            (Fields.Asin, TextJson(clean.Asin)),
            (Fields.Isbn, TextJson(clean.Isbn)),
        ];

        var rows = new List<AudiobookField>();
        foreach (var (field, valueJson) in candidate)
        {
            if (valueJson is null) continue;
            if (current.TryGetValue(field, out var existing)
                && (existing.Origin == "user" || (clean.Provider == "openlibrary" && existing.Provider == "audible")))
                continue;
            rows.Add(new AudiobookField { Field = field, ValueJson = valueJson, Origin = "provider", Provider = clean.Provider });
        }
        return rows;
    }

    private static string? TextJson(string? value) => value is null ? null : JsonSerializer.Serialize(value);

    /// <summary>The metadata field names, as in the API's JSON and the AudiobookField table.</summary>
    private static class Fields
    {
        public const string Title = "title", Subtitle = "subtitle", Authors = "authors", Narrators = "narrators",
            Genres = "genres", Series = "series", SeriesPart = "seriesPart", Publisher = "publisher", ReleaseDate = "releaseDate",
            Language = "language", Format = "format", Asin = "asin", Isbn = "isbn";
    }

    private static BookMatchDto ToDto(long groupId, MetadataMatch match) => new(
        groupId,
        JsonSerializer.Deserialize<MetadataCandidateDto>(match.CandidateJson, JsonSerializerOptions.Web)!,
        DateTimeOffset.Parse(match.PickedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The value trimmed (blank as null); an error under <paramref name="field"/> when it's too long.</summary>
    private static string? CleanText(Dictionary<string, string[]> errors, string field, string? value)
    {
        var trimmed = Trim(value);
        if (trimmed?.Length > GroupingService.MaxTextLength)
            errors[field] = [$"This value can be at most {GroupingService.MaxTextLength} characters."];
        return trimmed;
    }

    /// <summary>
    /// The names trimmed with blanks dropped; an error under <paramref name="field"/> for too many or too long. Errors
    /// call each item a "name", or an "entry" when <paramref name="entries"/> is set (genres).
    /// </summary>
    private static List<string> CleanPeople(
        Dictionary<string, string[]> errors, string field, IReadOnlyList<string>? names, bool entries = false)
    {
        var cleaned = (names ?? []).Select(Trim).OfType<string>().ToList();
        var (one, many) = entries ? ("entry", "entries") : ("name", "names");
        if (cleaned.Count > MaxPeople) errors[field] = [$"There can be at most {MaxPeople} {many}."];
        else if (cleaned.Any(n => n.Length > GroupingService.MaxTextLength))
            errors[field] = [$"Each {one} can be at most {GroupingService.MaxTextLength} characters."];
        return cleaned;
    }

    /// <summary>The candidate trimmed (blank text as null, blank names dropped), plus errors keyed by JSON field name.</summary>
    private static (MetadataCandidateDto Clean, Dictionary<string, string[]> Errors) Clean(MetadataCandidateDto c)
    {
        var errors = new Dictionary<string, string[]>();

        string? Text(string field, string? value) => CleanText(errors, field, value);
        List<string> People(string field, IReadOnlyList<string>? names) => CleanPeople(errors, field, names);

        var provider = Trim(c.Provider);
        if (provider is null || !Providers.Contains(provider)) errors["provider"] = ["The provider must be audible or openlibrary."];

        var title = Text("title", c.Title);
        if (title is null) errors["title"] = ["The title is required."];

        SeriesRefDto? series = null;
        if (c.Series is not null && Text("series.name", c.Series.Name) is { } seriesName)
            series = new SeriesRefDto(seriesName, Text("series.part", c.Series.Part));

        var coverUrl = Text("coverUrl", c.CoverUrl);
        if (coverUrl is not null && !(Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri)
                                       && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            errors["coverUrl"] = ["The cover URL must be an absolute http or https URL."];

        if (c.RuntimeMinutes < 0) errors["runtimeMinutes"] = ["The runtime can't be negative."];

        var clean = new MetadataCandidateDto(
            provider ?? "",
            Text("asin", c.Asin),
            title,
            Text("subtitle", c.Subtitle),
            People("authors", c.Authors),
            People("narrators", c.Narrators),
            series,
            Text("publisher", c.Publisher),
            Text("releaseDate", c.ReleaseDate),
            Text("language", c.Language),
            c.RuntimeMinutes,
            Text("format", c.Format),
            coverUrl,
            Text("isbn", c.Isbn));
        return (clean, errors);
    }
}
