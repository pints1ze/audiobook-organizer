using System.Text.Json;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Audible;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Searches Audible's unofficial, keyless catalog API (.com marketplace). The HttpClient's base address
/// and timeout are set where it's registered in Program.cs.
/// </summary>
public class AudibleClient(HttpClient http) : IMetadataProvider
{
    private const string SearchPath =
        "1.0/catalog/products?response_groups=contributors,media,product_desc,product_attrs,product_extended_attrs,series" +
        "&image_sizes=500&num_results=50&products_sort_by=Relevance&keywords=";

    public string Name => "audible";

    public async Task<IReadOnlyList<MetadataCandidateDto>> SearchAsync(string keywords, CancellationToken ct)
    {
        AudibleCatalogResponse? response;
        try
        {
            using var message = await http.GetAsync(SearchPath + Uri.EscapeDataString(keywords), ct);
            if (!message.IsSuccessStatusCode)
                throw new MetadataProviderException($"Audible search failed: HTTP {(int)message.StatusCode} {message.ReasonPhrase}.");

            await using var stream = await message.Content.ReadAsStreamAsync(ct);
            response = await JsonSerializer.DeserializeAsync<AudibleCatalogResponse>(stream, cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MetadataProviderException($"Audible search failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MetadataProviderException("Audible search failed: the request timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new MetadataProviderException("Audible search failed: the response wasn't readable JSON.", ex);
        }

        return (response?.Products ?? []).Select(ToCandidate).ToList();
    }

    private MetadataCandidateDto ToCandidate(AudibleProduct p)
    {
        var series = p.Series?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title));
        return new MetadataCandidateDto(
            Name,
            Clean(p.Asin),
            Clean(p.Title),
            Clean(p.Subtitle),
            Names(p.Authors),
            Names(p.Narrators),
            series is null ? null : new SeriesRefDto(series.Title!.Trim(), Clean(series.Sequence)),
            Clean(p.PublisherName),
            Clean(p.ReleaseDate),
            Clean(p.Language),
            p.RuntimeLengthMin,
            Clean(p.FormatType),
            Cover(p.ProductImages));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> Names(List<AudibleContributor>? contributors) =>
        (contributors ?? []).Select(c => Clean(c.Name)).OfType<string>().ToList();

    /// <summary>The 500px image when present, otherwise the largest one.</summary>
    private static string? Cover(Dictionary<string, string>? images)
    {
        if (images is null || images.Count == 0) return null;
        if (images.TryGetValue("500", out var preferred) && !string.IsNullOrWhiteSpace(preferred)) return preferred.Trim();
        return images
            .Where(i => !string.IsNullOrWhiteSpace(i.Value))
            .OrderByDescending(i => int.TryParse(i.Key, out var size) ? size : 0)
            .Select(i => i.Value.Trim())
            .FirstOrDefault();
    }
}
