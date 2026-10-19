using System.Text.Json.Serialization;

namespace AudiobookOrganizer.Api.Models.Audible;

// Shapes from GET api.audible.com/1.0/catalog/products (unofficial). Only the fields we use; see docs/fixtures/audible/.

public class AudibleCatalogResponse
{
    [JsonPropertyName("products")] public List<AudibleProduct> Products { get; set; } = [];
}

public class AudibleProduct
{
    [JsonPropertyName("asin")] public string? Asin { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
    [JsonPropertyName("authors")] public List<AudibleContributor>? Authors { get; set; }
    [JsonPropertyName("narrators")] public List<AudibleContributor>? Narrators { get; set; }
    [JsonPropertyName("series")] public List<AudibleSeries>? Series { get; set; }
    [JsonPropertyName("publisher_name")] public string? PublisherName { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("runtime_length_min")] public int? RuntimeLengthMin { get; set; }
    [JsonPropertyName("format_type")] public string? FormatType { get; set; }

    /// <summary>Cover URLs keyed by pixel size, e.g. "500".</summary>
    [JsonPropertyName("product_images")] public Dictionary<string, string>? ProductImages { get; set; }
}

public class AudibleContributor
{
    [JsonPropertyName("asin")] public string? Asin { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class AudibleSeries
{
    [JsonPropertyName("asin")] public string? Asin { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("sequence")] public string? Sequence { get; set; }
}
