using System.Text.Json.Serialization;

namespace AudiobookOrganizer.Api.Models.OpenLibrary;

// Shapes from GET openlibrary.org/search.json. Only the fields we request; see docs/fixtures/openlibrary/.

public class OpenLibrarySearchResponse
{
    [JsonPropertyName("docs")] public List<OpenLibraryDoc> Docs { get; set; } = [];
}

/// <summary>A work (not an edition), so its data mixes print editions.</summary>
public class OpenLibraryDoc
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
    [JsonPropertyName("author_name")] public List<string>? AuthorName { get; set; }
    [JsonPropertyName("first_publish_year")] public int? FirstPublishYear { get; set; }
    [JsonPropertyName("cover_i")] public long? CoverI { get; set; }
}
