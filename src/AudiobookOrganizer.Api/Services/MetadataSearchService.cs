using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Tries the registered providers in order (Audible, then Open Library) and returns the first non-empty result.
/// Throws <see cref="MetadataProviderException"/> only when every provider failed.
/// </summary>
public class MetadataSearchService(IEnumerable<IMetadataProvider> providers, ILogger<MetadataSearchService> logger)
{
    public async Task<MetadataSearchResultDto> SearchAsync(string keywords, CancellationToken ct)
    {
        var errors = new List<string>();
        var anySucceeded = false;

        foreach (var provider in providers)
        {
            try
            {
                var candidates = await provider.SearchAsync(keywords, ct);
                anySucceeded = true;
                if (candidates.Count > 0) return new MetadataSearchResultDto(candidates, errors);
            }
            catch (MetadataProviderException ex)
            {
                logger.LogWarning(ex, "Metadata search with {Provider} failed for {Keywords}", provider.Name, keywords);
                errors.Add(ex.Message);
            }
        }

        if (!anySucceeded && errors.Count > 0) throw new MetadataProviderException(string.Join(" ", errors));
        return new MetadataSearchResultDto([], errors);
    }

    /// <summary>
    /// True when the keywords are an ISBN-10 or ISBN-13 once hyphens and spaces are removed (an ISBN-10 may end in X).
    /// Checks the shape only, not the check digit.
    /// </summary>
    public static bool TryNormalizeIsbn(string keywords, out string isbn)
    {
        var compact = new string(keywords.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        var valid = compact.Length switch
        {
            13 => compact.All(char.IsAsciiDigit),
            10 => compact[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(compact[9]) || compact[9] == 'X'),
            _ => false,
        };
        isbn = valid ? compact : "";
        return valid;
    }
}
