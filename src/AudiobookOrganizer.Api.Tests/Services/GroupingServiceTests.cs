using System.Security.Cryptography;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Services.Grouping;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookOrganizer.Api.Tests.Services;

public class GroupingServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-grouping-{Guid.NewGuid():N}");

    private readonly string _incoming;
    private readonly SqliteConnectionFactory _connections;
    private readonly SettingsService _settings;
    private readonly ScanRepository _scans;
    private readonly GroupRepository _groups;
    private readonly GroupingService _grouping;

    public GroupingServiceTests()
    {
        _incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        var library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
        var bookA = Directory.CreateDirectory(Path.Combine(_incoming, "book a")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(bookA, "track1.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(bookA, "track2.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), Path.Combine(_incoming, "single.m4b"));

        _connections = new SqliteConnectionFactory(Path.Combine(_root, "grouping.db"));
        new DatabaseInitializer(_connections).Initialize();
        _settings = new SettingsService(new SettingsRepository(_connections));
        Assert.Empty(_settings.Save(new SettingsDto(_incoming, library, false)).Errors);
        _scans = new ScanRepository(_connections);
        _groups = new GroupRepository(_connections);
        _grouping = new GroupingService(_settings, _scans, _groups);
    }

    private ScanService NewScanService(GroupingService grouping) =>
        new(_settings, _scans, AudioFileReaderTests.NewReader(), grouping, NullLogger<ScanService>.Instance);

    /// <summary>Each stored group's files as relative paths in position order.</summary>
    private List<List<string>> StoredGroups()
    {
        using var connection = _connections.Open();
        return connection.Query<(long GroupId, string SourcePath)>(
                """
                SELECT g.GroupId, a.SourcePath FROM FileGroupFile g JOIN AudioFile a ON a.Id = g.AudioFileId
                ORDER BY g.GroupId, g.Position
                """)
            .GroupBy(r => r.GroupId)
            .Select(g => g.Select(r => Path.GetRelativePath(_incoming, r.SourcePath).Replace('\\', '/')).ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The positions stored in the group that holds <paramref name="relativePath"/>.</summary>
    private List<long> Positions(string relativePath)
    {
        using var connection = _connections.Open();
        return connection.Query<long>(
            """
            SELECT g.Position FROM FileGroupFile g
            WHERE g.GroupId = (SELECT g2.GroupId FROM FileGroupFile g2 JOIN AudioFile a2 ON a2.Id = g2.AudioFileId
                               WHERE a2.SourcePath = @path)
            ORDER BY g.Position
            """,
            new { path = Path.Combine(_incoming, relativePath.Replace('/', Path.DirectorySeparatorChar)) }).ToList();
    }

    [Fact]
    public void Completed_scan_stores_the_engines_groups_with_ordered_files()
    {
        var run = NewScanService(_grouping).Run(CancellationToken.None);

        Assert.Equal("Completed", run.Status);
        Assert.Equal(
            [["book a/track1.mp3", "book a/track2.mp3"], ["single.m4b"]],
            StoredGroups());
        Assert.Equal([1, 2], Positions("book a/track1.mp3"));
        Assert.Equal([1], Positions("single.m4b"));

        var list = _grouping.List();
        Assert.Equal(2, list.Groups.Count);
        Assert.Empty(list.Unresolved);
        var book = list.Groups.Single(g => g.FileCount == 2);
        Assert.InRange(book.TotalDurationMs, 59_000, 61_000);
        Assert.Contains(book.Evidence, e => e.Contains("book a"));
    }

    [Fact]
    public void Rescan_replaces_groups_and_excludes_files_with_a_scan_error()
    {
        File.WriteAllBytes(Path.Combine(_incoming, "broken.mp3"), RandomNumberGenerator.GetBytes(4096));
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);

        var second = scanner.Run(CancellationToken.None);

        Assert.Equal("Completed", second.Status);
        Assert.Equal(1, second.FilesFailed);
        Assert.Equal(2, StoredGroups().Count);
        Assert.DoesNotContain(StoredGroups(), g => g.Contains("broken.mp3"));
        using (var connection = _connections.Open())
        {
            Assert.Equal(2, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM FileGroup"));
            Assert.Equal(3, connection.ExecuteScalar<long>("SELECT COUNT(*) FROM FileGroupFile"));
            Assert.All(connection.Query<long>("SELECT ScanRunId FROM FileGroup"), id => Assert.Equal(second.Id, id));
        }
        Assert.Empty(_grouping.List().Unresolved);
    }

    [Fact]
    public void When_grouping_throws_the_run_fails_and_previous_groups_stay()
    {
        NewScanService(_grouping).Run(CancellationToken.None);
        var before = StoredGroups();

        var run = NewScanService(new ThrowingGroupingService(_settings, _scans, _groups)).Run(CancellationToken.None);

        Assert.Equal("Failed", run.Status);
        Assert.StartsWith("Grouping failed", run.Error);
        Assert.Contains("boom", run.Error);
        Assert.Equal(before, StoredGroups());
    }

    [Fact]
    public void ReplaceAll_rolls_back_when_an_insert_fails()
    {
        var run = NewScanService(_grouping).Run(CancellationToken.None);
        var before = StoredGroups();

        // No AudioFile has Id 999999, so the foreign key fails after the delete has already run.
        var bad = new ProposedGroup("Bad", null, GroupConfidence.Low, [], [new GroupedFile(999_999, null, null)], 0);

        Assert.Throws<SqliteException>(() => _groups.ReplaceAll([bad], run.Id));
        Assert.Equal(before, StoredGroups());
    }

    private sealed class ThrowingGroupingService(SettingsService settings, ScanRepository scans, GroupRepository groups)
        : GroupingService(settings, scans, groups)
    {
        public override void Regroup(long scanRunId) => throw new InvalidOperationException("boom");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
