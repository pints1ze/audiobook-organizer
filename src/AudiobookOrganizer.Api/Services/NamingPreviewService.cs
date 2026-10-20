using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Shows where a book's files would land under the saved templates. Read-only: nothing is written or created.</summary>
public class NamingPreviewService(AudiobookService audiobooks, GroupRepository groups, SettingsService settings)
{
    public const string NoLibraryError = "The library folder isn't set.";
    public const string OutsideLibraryError = "The path would land outside the library.";

    /// <summary>The book's files in book order with their destinations; null when the group id is unknown.</summary>
    public BookPreviewDto? Preview(long groupId)
    {
        if (audiobooks.GetMetadata(groupId) is not { } metadata) return null;
        var people = audiobooks.GetPeople(groupId)!;
        var artist = CanonicalName(people.Authors);
        var narrator = CanonicalName(people.Narrators);
        var saved = settings.Get();
        var library = string.IsNullOrWhiteSpace(saved.LibraryPath) ? null : saved.LibraryPath;

        var files = groups.GetGroupFiles(groupId).Select((file, index) =>
        {
            var tokens = BookTokens.For(metadata.Fields, artist, narrator, index + 1, file.Disc);
            var directory = NamingTemplate.Render(saved.DirectoryTemplate!, tokens);
            var name = NamingTemplate.Render(saved.FileTemplate!, tokens);
            var missing = directory.Missing.Concat(name.Missing).Distinct().ToList();

            var error = directory.Error ?? name.Error ?? (library is null ? NoLibraryError : null);
            if (error is not null) return new FilePreviewDto(file.FileId, file.SourcePath, null, null, missing, error);

            var relative = $"{directory.Path}/{name.Path}{Path.GetExtension(file.SourcePath).ToLowerInvariant()}";
            return NamingTemplate.ResolveUnder(library!, relative) is { } destination
                ? new FilePreviewDto(file.FileId, file.SourcePath, relative, destination, missing, null)
                : new FilePreviewDto(file.FileId, file.SourcePath, null, null, missing, OutsideLibraryError);
        }).ToList();

        return new BookPreviewDto(groupId, library, files);
    }

    private static string? CanonicalName(IReadOnlyList<NameLinkDto> links) =>
        links.Count == 0 ? null : links[0].Entry?.Name ?? links[0].Name;
}
