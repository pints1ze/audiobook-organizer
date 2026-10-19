using System.Globalization;
using System.Text.Json;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.OpenLibrary;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Searches Open Library (keyless), the fallback provider. Results are works with print data, so only
/// title, subtitle, authors, first publish year and cover are mapped. Base address, timeout and
/// User-Agent are set where it's registered in Program.cs.
/// </summary>
public class OpenLibraryClient(HttpClient http) : IMetadataProvider
{
    private const string SearchPath = "search.json?limit=20&fields=key,title,subtitle,author_name,first_publish_year,cover_i&";

    public string Name => "openlibrary";

    public async Task<IReadOnlyList<MetadataCandidateDto>> SearchAsync(string keywords, CancellationToken ct)
    {
        var isIsbn = MetadataSearchService.TryNormalizeIsbn(keywords, out var isbn);
        var query = isIsbn ? "isbn=" + Uri.EscapeDataString(isbn) : "q=" + Uri.EscapeDataString(keywords);

        OpenLibrarySearchResponse? response;
        try
        {
            using var message = await http.GetAsync(SearchPath + query, ct);
            if (!message.IsSuccessStatusCode)
                throw new MetadataProviderException($"Open Library search failed: HTTP {(int)message.StatusCode} {message.ReasonPhrase}.");

            await using var stream = await message.Content.ReadAsStreamAsync(ct);
            response = await JsonSerializer.DeserializeAsync<OpenLibrarySearchResponse>(stream, cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            throw new MetadataProviderException($"Open Library search failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MetadataProviderException("Open Library search failed: the request timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new MetadataProviderException("Open Library search failed: the response wasn't readable JSON.", ex);
        }

        return (response?.Docs ?? []).Select(d => ToCandidate(d, isIsbn ? isbn : null)).ToList();
    }

    private MetadataCandidateDto ToCandidate(OpenLibraryDoc d, string? isbn) => new(
        Name,
        Asin: null,
        Clean(d.Title),
        Clean(d.Subtitle),
        (d.AuthorName ?? []).Select(Clean).OfType<string>().ToList(),
        Narrators: [],
        Series: null,
        Publisher: null,
        d.FirstPublishYear?.ToString(CultureInfo.InvariantCulture),
        Language: null,
        RuntimeMinutes: null,
        Format: null,
        d.CoverI is { } id ? $"https://covers.openlibrary.org/b/id/{id}-L.jpg" : null,
        isbn);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
