using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services.Grouping;

/// <summary>The renamed group, or errors keyed by JSON field name. No group and no errors means the id is unknown.</summary>
public record GroupRenameResult(GroupSummaryDto? Group, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>Runs the grouping engine over a scan's files and stores the result; lists the stored groups for the API.</summary>
public class GroupingService(SettingsService settings, ScanRepository scans, GroupRepository groups)
{
    public const int MaxTextLength = 500;

    /// <summary>
    /// Groups the error-free files seen in <paramref name="scanRunId"/> that aren't in a user-corrected group, and
    /// replaces the stored groups the user hasn't corrected.
    /// </summary>
    /// <remarks>Virtual so tests can make grouping fail.</remarks>
    public virtual void Regroup(long scanRunId)
    {
        var root = IncomingRoot();
        var inputs = groups.GetGroupableFiles(scanRunId)
            .Select(f => new GroupingInput(
                f.Id, RelativePath(root, f.SourcePath), f.Extension, f.Title, f.Album, f.Artist, f.AlbumArtist,
                f.Track, f.Disc, f.DurationMs))
            .ToList();

        var result = GroupingEngine.Group(inputs);
        groups.ReplaceProposed(result.Groups, scanRunId);
    }

    /// <summary>Sets a group's title and author (trimmed, blank as null) and marks it user-corrected.</summary>
    public GroupRenameResult Rename(long id, UpdateGroupDto request)
    {
        var title = Clean(request.Title);
        var author = Clean(request.Author);

        var errors = new Dictionary<string, string[]>();
        if (title?.Length > MaxTextLength) errors["title"] = [$"The title can be at most {MaxTextLength} characters."];
        if (author?.Length > MaxTextLength) errors["author"] = [$"The author can be at most {MaxTextLength} characters."];
        if (errors.Count > 0 || !groups.UpdateTitleAuthor(id, title, author)) return new GroupRenameResult(null, errors);

        return new GroupRenameResult(ToSummary(groups.GetGroup(id)!), errors);
    }

    /// <summary>The stored groups, and the files from the latest completed scan that are in none of them.</summary>
    public GroupListDto List()
    {
        var summaries = groups.GetGroups().Select(ToSummary).ToList();

        var latest = scans.GetLatestCompletedRun();
        var root = IncomingRoot();
        var unresolved = latest is null
            ? []
            : groups.GetUngroupedFiles(latest.Id)
                .Select(f => new UnresolvedFileDto(f.Id, RelativePath(root, f.SourcePath), f.DurationMs))
                .ToList();

        return new GroupListDto(summaries, unresolved);
    }

    private static GroupSummaryDto ToSummary(FileGroup g) => new(g.Id, g.Title, g.Author, g.Confidence,
        JsonSerializer.Deserialize<string[]>(g.EvidenceJson) ?? [], g.FileCount, g.TotalDurationMs, g.UserCorrected);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The saved incoming directory, normalised as ScanService does; null when it isn't set.</summary>
    private string? IncomingRoot()
    {
        var incoming = settings.Get().IncomingPath;
        return string.IsNullOrWhiteSpace(incoming) ? null : Path.GetFullPath(incoming);
    }

    private static string RelativePath(string? root, string sourcePath) =>
        (root is null ? sourcePath : Path.GetRelativePath(root, sourcePath)).Replace('\\', '/');
}
