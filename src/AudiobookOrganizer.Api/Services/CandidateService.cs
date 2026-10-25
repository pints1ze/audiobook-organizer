using System.Globalization;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Searches the catalog for a book on request and stores its top candidates with a score. Never applies a match.</summary>
public class CandidateService(GroupRepository groups, AudiobookService audiobooks, MetadataSearchService search,
    CandidateRepository candidates)
{
    public const int MaxStored = 5;
    public const string NothingToSearchFor = "No title or author to search for.";

    /// <summary>The group's last search; null when it was never searched. Check the group exists first.</summary>
    public BookCandidatesDto? Get(long groupId) => candidates.Get(groupId) is { } stored ? ToDto(stored) : null;

    public bool GroupExists(long groupId) => groups.GetGroup(groupId) is not null;

    /// <summary>
    /// Searches with the book's title and first author, scores every candidate and stores the top <see cref="MaxStored"/>
    /// (ties keep provider order), replacing the last search. A failed search is stored as an error with no candidates.
    /// Null when the group id is unknown.
    /// </summary>
    public async Task<BookCandidatesDto?> RefreshAsync(long groupId, CancellationToken ct)
    {
        if (groups.GetGroup(groupId) is not { } group) return null;
        var fields = audiobooks.GetMetadata(groupId)!.Fields;
        var title = Blank(fields.Title.Value) ?? Blank(group.Title);
        var authors = fields.Authors.Value.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (authors.Count == 0 && Blank(group.Author) is { } groupAuthor) authors.Add(groupAuthor);

        var keywords = string.Join(" ", new[] { title, authors.FirstOrDefault() }.Where(k => k is not null));
        IReadOnlyList<string> errors;
        List<MetadataCandidate> top = [];
        if (keywords.Length == 0) errors = [NothingToSearchFor];
        else
        {
            try
            {
                var result = await search.SearchAsync(keywords, ct);
                errors = result.ProviderErrors;
                top = result.Candidates
                    .Select(c => (Candidate: c, Score: Score(title, authors, group.TotalDurationMs, c)))
                    .OrderByDescending(s => s.Score)
                    .Take(MaxStored)
                    .Select(s => new MetadataCandidate
                    {
                        Provider = s.Candidate.Provider,
                        CandidateJson = JsonSerializer.Serialize(s.Candidate, JsonSerializerOptions.Web),
                        Score = s.Score,
                    })
                    .ToList();
            }
            catch (MetadataProviderException ex)
            {
                errors = [ex.Message];
            }
        }

        candidates.Replace(groupId, DateTimeOffset.UtcNow.ToString("O"), JsonSerializer.Serialize(errors), top);
        return Get(groupId);
    }

    /// <summary>
    /// 0–100: title up to 50 (the share of the book's title words in the candidate's title), author 25 (any author
    /// matches one of the candidate's by name key), runtime 25 within 3% of the duration or 12 within 10%.
    /// </summary>
    public static int Score(string? title, IReadOnlyList<string> authors, long durationMs, MetadataCandidateDto candidate)
    {
        var bookWords = Words(title);
        var candidateWords = Words(candidate.Title).ToHashSet();
        var titlePoints = bookWords.Count == 0
            ? 0
            : (int)Math.Round(50.0 * bookWords.Count(candidateWords.Contains) / bookWords.Count, MidpointRounding.AwayFromZero);

        var candidateAuthors = candidate.Authors.Select(CanonicalNameService.NameKey).ToHashSet();
        var authorPoints = authors.Any(a => candidateAuthors.Contains(CanonicalNameService.NameKey(a))) ? 25 : 0;

        var runtimePoints = 0;
        if (durationMs > 0 && candidate.RuntimeMinutes is int minutes and > 0)
        {
            var off = Math.Abs(minutes * 60_000.0 - durationMs) / durationMs;
            runtimePoints = off <= 0.03 ? 25 : off <= 0.10 ? 12 : 0;
        }

        return titlePoints + authorPoints + runtimePoints;
    }

    /// <summary>Lower-cased words, apostrophes dropped and other punctuation read as a space.</summary>
    private static List<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var letters = text.ToLowerInvariant().Where(c => c is not ('\'' or '’'))
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return new string(letters).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BookCandidatesDto ToDto((CandidateSearch Search, IReadOnlyList<MetadataCandidate> Candidates) stored) => new(
        stored.Search.GroupId,
        DateTimeOffset.Parse(stored.Search.SearchedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        stored.Candidates.Select(c => new ScoredCandidateDto(
            JsonSerializer.Deserialize<MetadataCandidateDto>(c.CandidateJson, JsonSerializerOptions.Web)!, c.Score)).ToList(),
        JsonSerializer.Deserialize<List<string>>(stored.Search.ErrorsJson) ?? []);
}
