namespace AudiobookOrganizer.Api.Models.Api;

public record SettingsDto(string? IncomingPath, string? LibraryPath, bool KeepSourceFiles);
