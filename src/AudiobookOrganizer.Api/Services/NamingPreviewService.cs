using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Shows where books' files would land under the saved templates. Read-only: nothing is written or created.</summary>
public class NamingPreviewService(AudiobookService audiobooks, GroupRepository groups, SettingsService settings)
{
    public const string NoLibraryError = "The library folder isn't set.";
    public const string OutsideLibraryError = "The path would land outside the library.";

    /// <summary>
    /// Every group's book preview in <see cref="GroupRepository.GetGroups"/> order, with duplicate destinations
    /// (compared ignoring case, across all books) and destinations already on disk flagged. Only reads the library.
    /// </summary>
    public LibraryPreviewDto PreviewAll()
    {
        var saved = settings.Get();
        var library = string.IsNullOrWhiteSpace(saved.LibraryPath) ? null : saved.LibraryPath;
        var books = groups.GetGroups()
            .Select(group => Book(group, saved, library))
            .OfType<BookPreviewDto>()
            .ToList();

        var duplicates = books.SelectMany(b => b.Files)
            .Where(f => f.DestinationPath is not null)
            .GroupBy(f => f.DestinationPath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        books = books.Select(b => b with
        {
            Files = b.Files.Select(f => f.DestinationPath is { } destination
                ? f with
                {
                    Duplicate = duplicates.Contains(destination),
                    Exists = File.Exists(destination) || Directory.Exists(destination),
                }
                : f).ToList(),
        }).ToList();

        var files = books.SelectMany(b => b.Files).ToList();
        return new LibraryPreviewDto(library, books, new PreviewCountsDto(
            files.Count,
            files.Count(f => f.Missing.Count > 0),
            files.Count(f => f.Error is not null),
            files.Count(f => f.Duplicate),
            files.Count(f => f.Exists)));
    }

    /// <summary>
    /// The book's entry from <see cref="PreviewAll"/>, so its flags count every other book; null when the group id is
    /// unknown.
    /// </summary>
    // shortcut: builds the whole library preview for one book — revisit if the preview is slow on the real collection.
    public BookPreviewDto? Preview(long groupId) => PreviewAll().Books.FirstOrDefault(b => b.GroupId == groupId);

    /// <summary>The group's files in book order with their destinations; null if the group vanished meanwhile.</summary>
    private BookPreviewDto? Book(FileGroup group, SettingsDto saved, string? library)
    {
        if (audiobooks.GetMetadata(group.Id) is not { } metadata) return null;
        var people = audiobooks.GetPeople(metadata);
        var artist = CanonicalName(people.Authors);
        var narrator = CanonicalName(people.Narrators);

        var files = groups.GetGroupFiles(group.Id).Select((file, index) =>
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

        return new BookPreviewDto(group.Id, library, group.Title, group.Author, files);
    }

    private static string? CanonicalName(IReadOnlyList<NameLinkDto> links) =>
        links.Count == 0 ? null : links[0].Entry?.Name ?? links[0].Name;
}
