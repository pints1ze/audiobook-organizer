namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// Where one file of a book would go. RelativePath uses '/' and ends in the source's lower-case extension;
/// DestinationPath is it resolved under the library. Both are null when Error is set. Missing lists the required
/// tokens without a value, from both templates, in template order. Duplicate: another previewed file has the same
/// destination (ignoring case). Exists: a file or folder is already at the destination. Both are false when Error is set.
/// </summary>
public record FilePreviewDto(
    long FileId, string SourcePath, string? RelativePath, string? DestinationPath, IReadOnlyList<string> Missing,
    string? Error, bool Duplicate = false, bool Exists = false);

/// <summary>
/// A book's (a group's) files in book order, each with the destination the saved templates give it. Title and Author
/// are the group's, as GET /api/groups shows them.
/// </summary>
public record BookPreviewDto(
    long GroupId, string? LibraryPath, string? Title, string? Author, IReadOnlyList<FilePreviewDto> Files);

/// <summary>File counts across the preview: files with missing tokens, with an error, flagged duplicate, flagged existing.</summary>
public record PreviewCountsDto(int Files, int Missing, int Errors, int Duplicates, int Existing);

/// <summary>Every book's preview in GET /api/groups order, with the counts.</summary>
public record LibraryPreviewDto(string? LibraryPath, IReadOnlyList<BookPreviewDto> Books, PreviewCountsDto Counts);
