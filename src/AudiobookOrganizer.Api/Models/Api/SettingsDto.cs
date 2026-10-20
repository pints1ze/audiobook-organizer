namespace AudiobookOrganizer.Api.Models.Api;

public record SettingsDto(
    string? IncomingPath, string? LibraryPath, string? DirectoryTemplate, string? FileTemplate, bool KeepSourceFiles);
