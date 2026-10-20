namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>
/// Where one file of a book would go. RelativePath uses '/' and ends in the source's lower-case extension;
/// DestinationPath is it resolved under the library. Both are null when Error is set. Missing lists the required
/// tokens without a value, from both templates, in template order.
/// </summary>
public record FilePreviewDto(
    long FileId, string SourcePath, string? RelativePath, string? DestinationPath, IReadOnlyList<string> Missing,
    string? Error);

/// <summary>A book's (a group's) files in book order, each with the destination the saved templates give it.</summary>
public record BookPreviewDto(long GroupId, string? LibraryPath, IReadOnlyList<FilePreviewDto> Files);
