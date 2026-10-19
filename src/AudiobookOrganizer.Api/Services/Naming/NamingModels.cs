namespace AudiobookOrganizer.Api.Services.Naming;

/// <summary>
/// The outcome of rendering a naming template. Path is relative, uses '/' between segments and has no extension;
/// it is null when Error is set. Missing lists required token names (without '%') that had no value, in template order.
/// </summary>
public record TemplateResult(string? Path, IReadOnlyList<string> Missing, string? Error);
