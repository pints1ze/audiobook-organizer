using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Services;

public record SettingsSaveResult(SettingsDto? Saved, IReadOnlyDictionary<string, string[]> Errors);

public class SettingsService(SettingsRepository repository)
{
    // Error keys match the camelCase JSON field names the client sends.
    public const string IncomingField = "incomingPath";
    public const string LibraryField = "libraryPath";
    public const string DirectoryTemplateField = "directoryTemplate";
    public const string FileTemplateField = "fileTemplate";

    public const string DefaultDirectoryTemplate = "%artist%/[%series%/Book %series-part:2% - ]%album%";
    public const string DefaultFileTemplate = "[%track:3%. ]%album% (%artist%)";

    // Every token gets a value, so Render reports only structural errors (Missing and empty paths depend on a book).
    private static readonly IReadOnlyDictionary<string, string?> SampleValues =
        NamingTemplate.Tokens.ToDictionary(
            token => token,
            token => (string?)(token is "track" or "disc" or "series-part" or "year" ? "1" : "x"));

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public SettingsDto Get() => new(
        repository.Get(SettingsRepository.IncomingPath),
        repository.Get(SettingsRepository.LibraryPath),
        repository.Get(SettingsRepository.DirectoryTemplate) ?? DefaultDirectoryTemplate,
        repository.Get(SettingsRepository.FileTemplate) ?? DefaultFileTemplate,
        repository.Get(SettingsRepository.KeepSourceFiles) == "true");

    public SettingsSaveResult Save(SettingsDto request)
    {
        var errors = new Dictionary<string, string[]>();
        var incoming = request.IncomingPath?.Trim();
        var library = request.LibraryPath?.Trim();
        var directoryTemplate = request.DirectoryTemplate?.Trim();
        var fileTemplate = request.FileTemplate?.Trim();

        AddError(errors, IncomingField, ValidateDirectory(incoming, "incoming"));
        AddError(errors, LibraryField, ValidateDirectory(library, "library"));

        if (errors.Count == 0) AddError(errors, LibraryField, ValidateNoOverlap(incoming!, library!));

        // Only probe once the library is known not to sit inside incoming: nothing is ever written into incoming.
        if (errors.Count == 0) AddError(errors, LibraryField, ValidateWritable(library!));

        AddError(errors, DirectoryTemplateField, ValidateTemplate(directoryTemplate, "directory"));
        AddError(errors, FileTemplateField, ValidateTemplate(fileTemplate, "file")
            ?? (fileTemplate!.IndexOfAny(['/', '\\']) >= 0
                ? "The file template can't contain folders; put them in the directory template."
                : null));

        if (errors.Count > 0) return new SettingsSaveResult(null, errors);

        var saved = new SettingsDto(incoming, library, directoryTemplate, fileTemplate, request.KeepSourceFiles);
        repository.SetMany(new Dictionary<string, string>
        {
            [SettingsRepository.IncomingPath] = saved.IncomingPath!,
            [SettingsRepository.LibraryPath] = saved.LibraryPath!,
            [SettingsRepository.DirectoryTemplate] = saved.DirectoryTemplate!,
            [SettingsRepository.FileTemplate] = saved.FileTemplate!,
            [SettingsRepository.KeepSourceFiles] = saved.KeepSourceFiles ? "true" : "false",
        });
        return new SettingsSaveResult(saved, errors);
    }

    private static string? ValidateDirectory(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) return $"Enter the {label} directory.";
        if (!Path.IsPathFullyQualified(path)) return $"The {label} directory must be a full path, not a relative one.";
        if (File.Exists(path)) return $"The {label} path points to a file, not a directory.";
        if (!Directory.Exists(path)) return $"The {label} directory doesn't exist.";

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return $"The {label} directory can't be read.";
        }

        return null;
    }

    private static string? ValidateTemplate(string? template, string label) =>
        string.IsNullOrEmpty(template)
            ? $"Enter the {label} template."
            : NamingTemplate.Render(template, SampleValues).Error;

    private static string? ValidateNoOverlap(string incoming, string library)
    {
        var a = Normalize(incoming);
        var b = Normalize(library);
        if (string.Equals(a, b, PathComparison)) return "The library directory must be different from the incoming directory.";
        if (b.StartsWith(a, PathComparison)) return "The library directory can't be inside the incoming directory.";
        if (a.StartsWith(b, PathComparison)) return "The incoming directory can't be inside the library directory.";
        return null;
    }

    private static string? ValidateWritable(string library)
    {
        var probe = Path.Combine(library, $".audiobook-organizer-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return "The library directory can't be written to.";
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // The probe couldn't be removed; validation still reports the write result.
            }
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;

    private static void AddError(Dictionary<string, string[]> errors, string field, string? message)
    {
        if (message is not null) errors[field] = [message];
    }
}
