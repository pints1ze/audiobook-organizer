using System.Globalization;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;

namespace AudiobookOrganizer.Api.Services;

/// <summary>A picture read from a file: its bytes and MIME type as the tag records it.</summary>
public record EmbeddedPicture(byte[] Data, string MimeType);

/// <summary>The book's covers after a choice, or errors keyed by JSON field name. Neither means the group id is unknown.</summary>
public record ChooseCoverResult(BookCoverDto? Cover, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>
/// Lists a book's embedded covers and its match's cover, and stores the one the user picks. Never writes a source file.
/// </summary>
public class CoverService(GroupingService grouping, GroupRepository groups, AudiobookRepository audiobooks,
    CoverDownloadClient downloads)
{
    public const int MaxCoverBytes = 10 * 1024 * 1024;

    // shortcut: only the first URL is checked; HttpClient follows redirects (Open Library's go to archive.org) and
    // refuses https -> http — revisit if another catalog is added.
    private static readonly string[] CoverHosts = ["m.media-amazon.com", "covers.openlibrary.org"];

    /// <summary>The group's chosen cover and its files with an embedded cover; null when the group id is unknown.</summary>
    public BookCoverDto? GetCovers(long groupId)
    {
        // GetDetail gives the files' paths relative to incoming, as the rest of the API shows them.
        if (grouping.GetDetail(groupId) is not { } detail) return null;
        var withCover = audiobooks.GetCoverFiles(groupId).Select(f => f.FileId).ToHashSet();
        var embedded = detail.Files.Where(f => withCover.Contains(f.FileId))
            .Select(f => new EmbeddedCoverDto(f.FileId, f.RelativePath)).ToList();
        return new BookCoverDto(groupId, audiobooks.GetCover(groupId) is { } cover ? ToDto(cover) : null, embedded,
            ProviderCover(groupId));
    }

    /// <summary>
    /// The first picture in the file, read with TagLibSharp and never saved, with its type as "image/jpeg" or
    /// "image/png"; null when the file isn't in the group, is missing or unreadable, or has no JPEG or PNG picture. The
    /// file is read again because it may have changed since the scan. Any other type the tag claims (text/html, SVG) is
    /// refused, so a crafted tag can't be served as a page on the app's origin.
    /// </summary>
    public EmbeddedPicture? ReadEmbedded(long groupId, long fileId) =>
        FilePath(groupId, fileId) is { } path && ReadPicture(path) is { } picture && ImageType(picture.MimeType) is { } type
            ? picture with { MimeType = type }
            : null;

    /// <summary>The group's chosen cover image; null when there's none.</summary>
    public AudiobookCover? GetChosenImage(long groupId) => audiobooks.GetCoverImage(groupId);

    /// <summary>
    /// Stores the book's cover (JPEG or PNG, at most 10 MB), creating the Audiobook if needed and marking the group
    /// user-corrected. Source "embedded" copies the picture of <paramref name="request"/>'s file; "provider" downloads
    /// the current match's cover URL (never a URL from the request), only from a known catalog host. Throws
    /// <see cref="CoverDownloadException"/> when the download fails; nothing is stored on any error.
    /// </summary>
    public async Task<ChooseCoverResult> ChooseCoverAsync(long groupId, ChooseCoverDto request, CancellationToken ct)
    {
        if (groups.GetGroup(groupId) is null) return new ChooseCoverResult(null, new Dictionary<string, string[]>());

        static ChooseCoverResult Error(string field, string message) =>
            new(null, new Dictionary<string, string[]> { [field] = [message] });

        AudiobookCover cover;
        if (request.Source == "embedded")
        {
            if (request.FileId is not { } fileId || FilePath(groupId, fileId) is not { } path)
                return Error("fileId", "This file isn't in the book.");
            if (ReadPicture(path) is not { } picture) return Error("fileId", "This file has no embedded cover.");
            if (ImageType(picture.MimeType) is not { } mimeType) return Error("fileId", "The cover must be a JPEG or PNG.");
            if (picture.Data.Length > MaxCoverBytes) return Error("fileId", "The cover can be at most 10 MB.");
            cover = new AudiobookCover { Source = "embedded", FileId = fileId, MimeType = mimeType, Image = picture.Data };
        }
        else if (request.Source == "provider")
        {
            if (ProviderCover(groupId) is not { } offered) return Error("source", "Choose a match with a cover first.");
            if (!Uri.TryCreate(offered.CoverUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                || !CoverHosts.Contains(url.IdnHost, StringComparer.OrdinalIgnoreCase))
                return Error("source", "The cover URL isn't from a known catalog.");
            var image = await downloads.DownloadAsync(url, ct);
            if (SignatureType(image) is not { } mimeType) return Error("source", "The cover must be a JPEG or PNG.");
            if (image.Length > MaxCoverBytes) return Error("source", "The cover can be at most 10 MB.");
            cover = new AudiobookCover { Source = "provider", FileId = null, MimeType = mimeType, Image = image };
        }
        else
        {
            return Error("source", "Choose a cover source.");
        }

        audiobooks.SaveCover(groupId, cover, DateTimeOffset.UtcNow.ToString("O"));
        return new ChooseCoverResult(GetCovers(groupId), new Dictionary<string, string[]>());
    }

    /// <summary>The current match's provider and cover URL; null when there's no match or it has no cover URL.</summary>
    private ProviderCoverDto? ProviderCover(long groupId)
    {
        if (audiobooks.GetMatch(groupId) is not { } match) return null;
        var candidate = JsonSerializer.Deserialize<MetadataCandidateDto>(match.CandidateJson, JsonSerializerOptions.Web);
        return string.IsNullOrWhiteSpace(candidate?.CoverUrl) ? null : new ProviderCoverDto(match.Provider, candidate.CoverUrl);
    }

    /// <summary>"image/jpeg" or "image/png" when the bytes start with a JPEG or PNG signature; else null.</summary>
    private static string? SignatureType(byte[] bytes) =>
        bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : null;

    /// <summary>The recorded path of the file when it's in the group; null otherwise.</summary>
    private string? FilePath(long groupId, long fileId) =>
        groups.GetGroupFiles(groupId).FirstOrDefault(f => f.FileId == fileId)?.SourcePath;

    private static EmbeddedPicture? ReadPicture(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var file = TagLib.File.Create(path);
            var picture = file.Tag.Pictures.FirstOrDefault();
            return picture is null ? null : new EmbeddedPicture(picture.Data.Data, picture.MimeType ?? "");
        }
        catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException)
        {
            return null;
        }
    }

    /// <summary>"image/jpeg" or "image/png" for a JPEG or PNG MIME type (case-insensitive, "image/jpg" too); else null.</summary>
    private static string? ImageType(string mimeType) => mimeType.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => "image/jpeg",
        "image/png" => "image/png",
        _ => null,
    };

    private static ChosenCoverDto ToDto(AudiobookCover cover) => new(
        cover.Source, cover.FileId, cover.MimeType,
        DateTimeOffset.Parse(cover.ChosenAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
