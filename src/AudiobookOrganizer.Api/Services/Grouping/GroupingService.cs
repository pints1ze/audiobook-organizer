using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services.Grouping;

/// <summary>Runs the grouping engine over a scan's files and stores the result; lists the stored groups for the API.</summary>
public class GroupingService(SettingsService settings, ScanRepository scans, GroupRepository groups)
{
    /// <summary>Groups the error-free files seen in <paramref name="scanRunId"/> and replaces the stored groups.</summary>
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
        // shortcut: regroup replaces every group — revisit in phase 4 when user-confirmed groups must survive rescans
        groups.ReplaceAll(result.Groups, scanRunId);
    }

    /// <summary>The stored groups, and the files from the latest completed scan that are in none of them.</summary>
    public GroupListDto List()
    {
        var summaries = groups.GetGroups()
            .Select(g => new GroupSummaryDto(g.Id, g.Title, g.Author, g.Confidence,
                JsonSerializer.Deserialize<string[]>(g.EvidenceJson) ?? [], g.FileCount, g.TotalDurationMs))
            .ToList();

        var latest = scans.GetLatestCompletedRun();
        var root = IncomingRoot();
        var unresolved = latest is null
            ? []
            : groups.GetUngroupedFiles(latest.Id)
                .Select(f => new UnresolvedFileDto(f.Id, RelativePath(root, f.SourcePath), f.DurationMs))
                .ToList();

        return new GroupListDto(summaries, unresolved);
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
