using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services.Grouping;

/// <summary>The renamed group, or errors keyed by JSON field name. No group and no errors means the id is unknown.</summary>
public record GroupRenameResult(GroupSummaryDto? Group, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>The reordered group, or errors keyed by JSON field name. No group and no errors means the id is unknown.</summary>
public record GroupReorderResult(GroupDetailDto? Group, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>The moved-into or created group, or errors keyed by JSON field name. No group and no errors means the id is unknown.</summary>
public record GroupFilesResult(GroupDetailDto? Group, IReadOnlyDictionary<string, string[]> Errors);

/// <summary>The new group list after an exclude or unresolve, or errors keyed by JSON field name.</summary>
public record GroupListResult(GroupListDto? List, IReadOnlyDictionary<string, string[]> Errors);

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
        var (title, author, errors) = CleanTitleAuthor(request.Title, request.Author);
        if (errors.Count > 0 || !groups.UpdateTitleAuthor(id, title, author)) return new GroupRenameResult(null, errors);

        return new GroupRenameResult(ToSummary(groups.GetGroup(id)!), errors);
    }

    /// <summary>The group with its files in position order; null when no group has <paramref name="id"/>.</summary>
    public GroupDetailDto? GetDetail(long id)
    {
        var group = groups.GetGroup(id);
        if (group is null) return null;

        var root = IncomingRoot();
        var files = groups.GetGroupFiles(id)
            .Select(f => new GroupFileDto(f.FileId, RelativePath(root, f.SourcePath), f.Position, f.Disc, f.Track, f.DurationMs))
            .ToList();
        var s = ToSummary(group);
        return new GroupDetailDto(s.Id, s.Title, s.Author, s.Confidence, s.Evidence, s.FileCount, s.TotalDurationMs,
            s.UserCorrected, files);
    }

    /// <summary>
    /// Sets the group's file order to <paramref name="request"/>'s ids, which must be exactly the group's files with no
    /// duplicates, and marks the group user-corrected.
    /// </summary>
    public GroupReorderResult Reorder(long id, ReorderGroupDto request)
    {
        var noErrors = new Dictionary<string, string[]>();
        if (groups.GetGroup(id) is null) return new GroupReorderResult(null, noErrors);

        var fileIds = request.FileIds ?? [];
        var current = groups.GetGroupFiles(id).Select(f => f.FileId).ToHashSet();
        string? error =
            fileIds.Count == 0 ? "List every file in the group, in the new order."
            : fileIds.Distinct().Count() != fileIds.Count ? "Each file can appear only once."
            : !current.SetEquals(fileIds) ? "The list must contain exactly the group's files."
            : null;
        if (error is not null) return new GroupReorderResult(null, new Dictionary<string, string[]> { ["fileIds"] = [error] });

        groups.SetOrder(id, fileIds);
        return new GroupReorderResult(GetDetail(id), noErrors);
    }

    /// <summary>
    /// Moves <paramref name="request"/>'s files (from other groups or unresolved) to the end of group
    /// <paramref name="id"/>. Emptied groups are deleted; the target and surviving sources are marked user-corrected.
    /// </summary>
    public GroupFilesResult MoveFiles(long id, MoveFilesDto request)
    {
        if (groups.GetGroup(id) is null) return new GroupFilesResult(null, new Dictionary<string, string[]>());

        var fileIds = request.FileIds ?? [];
        var error = FileIdsError(fileIds);
        if (error is null)
        {
            var inTarget = groups.GetGroupFiles(id).Select(f => f.FileId).ToHashSet();
            if (fileIds.Any(inTarget.Contains)) error = "A file is already in this group.";
        }
        if (error is not null) return new GroupFilesResult(null, new Dictionary<string, string[]> { ["fileIds"] = [error] });

        groups.MoveFiles(id, fileIds);
        return new GroupFilesResult(GetDetail(id), new Dictionary<string, string[]>());
    }

    /// <summary>Creates a user-made group from <paramref name="request"/>'s files, in order, taking them out of their groups.</summary>
    public GroupFilesResult CreateGroup(CreateGroupDto request)
    {
        var fileIds = request.FileIds ?? [];
        var (title, author, errors) = CleanTitleAuthor(request.Title, request.Author);
        if (FileIdsError(fileIds) is { } error) errors["fileIds"] = [error];
        if (errors.Count > 0) return new GroupFilesResult(null, errors);

        var id = groups.CreateGroup(title, author, fileIds, scans.GetLatestCompletedRun()!.Id);
        return new GroupFilesResult(GetDetail(id), errors);
    }

    /// <summary>Takes <paramref name="request"/>'s files out of their groups and excludes them; they stay excluded across rescans.</summary>
    public GroupListResult Exclude(MoveFilesDto request) =>
        SetUserState(request, "Excluded", s => s.UserState == "Excluded", "A file is already excluded.");

    /// <summary>
    /// Takes <paramref name="request"/>'s files out of their groups (or out of excluded: the undo) and keeps them
    /// unresolved across rescans.
    /// </summary>
    public GroupListResult Unresolve(MoveFilesDto request) =>
        SetUserState(request, "Unresolved", s => !s.Grouped && s.UserState != "Excluded", "A file is already unresolved.");

    private GroupListResult SetUserState(
        MoveFilesDto request, string state, Func<(string? UserState, bool Grouped), bool> alreadyThere, string alreadyMessage)
    {
        var fileIds = request.FileIds ?? [];
        var error = FileIdsError(fileIds);
        if (error is null && groups.GetUserStates(fileIds).Values.Any(alreadyThere)) error = alreadyMessage;
        if (error is not null) return new GroupListResult(null, new Dictionary<string, string[]> { ["fileIds"] = [error] });

        groups.SetUserState(fileIds, state);
        return new GroupListResult(List(), new Dictionary<string, string[]>());
    }

    /// <summary>
    /// Why <paramref name="fileIds"/> can't be moved: empty, duplicated, or not all error-free files from the latest
    /// completed scan. Null when they can.
    /// </summary>
    private string? FileIdsError(IReadOnlyList<long> fileIds)
    {
        if (fileIds.Count == 0) return "Choose at least one file.";
        if (fileIds.Distinct().Count() != fileIds.Count) return "Each file can appear only once.";
        var latest = scans.GetLatestCompletedRun();
        var movable = latest is null ? new HashSet<long>() : groups.GetMovableFileIds(latest.Id, fileIds);
        return movable.Count != fileIds.Count ? "Every file must be one the latest scan read without error." : null;
    }

    /// <summary>
    /// The stored groups, the files from the latest completed scan that are in none of them, and the files the user
    /// excluded.
    /// </summary>
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
        var excluded = latest is null
            ? []
            : groups.GetExcludedFiles(latest.Id)
                .Select(f => new ExcludedFileDto(f.Id, RelativePath(root, f.SourcePath), f.DurationMs))
                .ToList();

        return new GroupListDto(summaries, unresolved, excluded);
    }

    private static GroupSummaryDto ToSummary(FileGroup g) => new(g.Id, g.Title, g.Author, g.Confidence,
        JsonSerializer.Deserialize<string[]>(g.EvidenceJson) ?? [], g.FileCount, g.TotalDurationMs, g.UserCorrected);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims the title and author (blank as null) and checks their length; errors are keyed "title" and "author".</summary>
    private static (string? Title, string? Author, Dictionary<string, string[]> Errors) CleanTitleAuthor(string? title, string? author)
    {
        var (t, a) = (Clean(title), Clean(author));
        var errors = new Dictionary<string, string[]>();
        if (t?.Length > MaxTextLength) errors["title"] = [$"The title can be at most {MaxTextLength} characters."];
        if (a?.Length > MaxTextLength) errors["author"] = [$"The author can be at most {MaxTextLength} characters."];
        return (t, a, errors);
    }

    /// <summary>The saved incoming directory, normalised as ScanService does; null when it isn't set.</summary>
    private string? IncomingRoot()
    {
        var incoming = settings.Get().IncomingPath;
        return string.IsNullOrWhiteSpace(incoming) ? null : Path.GetFullPath(incoming);
    }

    private static string RelativePath(string? root, string sourcePath) =>
        (root is null ? sourcePath : Path.GetRelativePath(root, sourcePath)).Replace('\\', '/');
}
