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

/// <summary>Stores the catalog candidate the user picks for a book.</summary>
public class AudiobookService(GroupRepository groups, AudiobookRepository audiobooks)
{
    public const int MaxPeople = 50;

    private static readonly HashSet<string> Providers = ["audible", "openlibrary"];

    public GetMatchResult GetMatch(long groupId)
    {
        if (groups.GetGroup(groupId) is null) return new GetMatchResult(false, null);
        return new GetMatchResult(true, audiobooks.GetMatch(groupId) is { } match ? ToDto(groupId, match) : null);
    }

    /// <summary>
    /// Validates and trims <paramref name="candidate"/>, then stores it as the group's one current match, creating the
    /// group's Audiobook if needed and marking the group user-corrected.
    /// </summary>
    public PickMatchResult PickMatch(long groupId, MetadataCandidateDto candidate)
    {
        if (groups.GetGroup(groupId) is null) return new PickMatchResult(null, new Dictionary<string, string[]>());

        var (clean, errors) = Clean(candidate);
        if (errors.Count > 0) return new PickMatchResult(null, errors);

        audiobooks.SaveMatch(groupId, clean.Provider, clean.Asin ?? clean.Isbn,
            JsonSerializer.Serialize(clean, JsonSerializerOptions.Web), DateTimeOffset.UtcNow.ToString("O"));
        return new PickMatchResult(ToDto(groupId, audiobooks.GetMatch(groupId)!), errors);
    }

    private static BookMatchDto ToDto(long groupId, MetadataMatch match) => new(
        groupId,
        JsonSerializer.Deserialize<MetadataCandidateDto>(match.CandidateJson, JsonSerializerOptions.Web)!,
        DateTimeOffset.Parse(match.PickedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The candidate trimmed (blank text as null, blank names dropped), plus errors keyed by JSON field name.</summary>
    private static (MetadataCandidateDto Clean, Dictionary<string, string[]> Errors) Clean(MetadataCandidateDto c)
    {
        var errors = new Dictionary<string, string[]>();

        string? Text(string field, string? value)
        {
            var trimmed = Trim(value);
            if (trimmed?.Length > GroupingService.MaxTextLength)
                errors[field] = [$"This value can be at most {GroupingService.MaxTextLength} characters."];
            return trimmed;
        }

        List<string> People(string field, IReadOnlyList<string>? names)
        {
            var cleaned = (names ?? []).Select(Trim).OfType<string>().ToList();
            if (cleaned.Count > MaxPeople) errors[field] = [$"There can be at most {MaxPeople} names."];
            else if (cleaned.Any(n => n.Length > GroupingService.MaxTextLength))
                errors[field] = [$"Each name can be at most {GroupingService.MaxTextLength} characters."];
            return cleaned;
        }

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
