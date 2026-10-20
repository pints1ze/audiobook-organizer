using System.Globalization;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services.Naming;

/// <summary>Maps one file of a book to a value for every naming token. Pure: the people are resolved by the caller.</summary>
public static class BookTokens
{
    /// <param name="artist">The first author, already resolved to its canonical name when linked.</param>
    /// <param name="narrator">The first narrator, already resolved the same way.</param>
    /// <param name="position">The file's 1-based position in book order; it is the track number.</param>
    /// <param name="disc">The file's scanned disc tag.</param>
    public static IReadOnlyDictionary<string, string?> For(
        BookMetadataFieldsDto fields, string? artist, string? narrator, int position, int? disc)
    {
        var title = fields.Title.Value;
        var release = fields.ReleaseDate.Value?.Trim();
        var year = release is { Length: >= 4 } && release[..4].All(char.IsAsciiDigit) ? release[..4] : null;

        return new Dictionary<string, string?>
        {
            ["artist"] = artist,
            ["album"] = title,
            ["series"] = fields.Series.Value,
            ["series-part"] = fields.SeriesPart.Value,
            ["track"] = position.ToString(CultureInfo.InvariantCulture),
            ["disc"] = disc?.ToString(CultureInfo.InvariantCulture),
            ["title"] = title,
            ["year"] = year,
            ["narrator"] = narrator,
        };
    }
}
