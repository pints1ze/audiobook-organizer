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
    public void ReplaceProposed_rolls_back_when_an_insert_fails()
    {
        var run = NewScanService(_grouping).Run(CancellationToken.None);
        var before = StoredGroups();

        // No AudioFile has Id 999999, so the foreign key fails after the delete has already run.
        var bad = new ProposedGroup("Bad", null, GroupConfidence.Low, [], [new GroupedFile(999_999, null, null)], 0);

        Assert.Throws<SqliteException>(() => _groups.ReplaceProposed([bad], run.Id));
        Assert.Equal(before, StoredGroups());
    }

    /// <summary>The stored id of the group holding <paramref name="relativePath"/>.</summary>
    private long GroupIdOf(string relativePath)
    {
        using var connection = _connections.Open();
        return connection.ExecuteScalar<long>(
            "SELECT g.GroupId FROM FileGroupFile g JOIN AudioFile a ON a.Id = g.AudioFileId WHERE a.SourcePath = @path",
            new { path = Path.Combine(_incoming, relativePath.Replace('/', Path.DirectorySeparatorChar)) });
    }

    [Fact]
    public void Renamed_group_keeps_its_id_title_author_and_files_after_a_rescan()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var id = GroupIdOf("book a/track1.mp3");

        var renamed = _grouping.Rename(id, new UpdateGroupDto("My Book", "Me"));
        Assert.Empty(renamed.Errors);
        Assert.True(renamed.Group!.UserCorrected);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);

        var list = _grouping.List();
        var group = Assert.Single(list.Groups, g => g.Id == id);
        Assert.Equal(("My Book", "Me", true, 2), (group.Title, group.Author, group.UserCorrected, group.FileCount));
        Assert.Single(StoredGroups(), g => g.Contains("book a/track1.mp3") || g.Contains("book a/track2.mp3"));
        Assert.Contains(["book a/track1.mp3", "book a/track2.mp3"], StoredGroups());
        Assert.Empty(list.Unresolved);
        Assert.All(list.Groups.Where(g => g.Id != id), g => Assert.False(g.UserCorrected));
    }

    [Fact]
    public void After_a_rename_new_files_are_still_grouped_and_uncorrected_groups_are_replaced()
    {
        var scanner = NewScanService(_grouping);
        var first = scanner.Run(CancellationToken.None);
        var id = GroupIdOf("book a/track1.mp3");
        var singleId = GroupIdOf("single.m4b");
        Assert.NotNull(_grouping.Rename(id, new UpdateGroupDto("My Book", null)).Group);

        var bookB = Directory.CreateDirectory(Path.Combine(_incoming, "book b")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), Path.Combine(bookB, "new.m4b"));
        var second = scanner.Run(CancellationToken.None);

        Assert.Equal("Completed", second.Status);
        Assert.Equal(
            [["book a/track1.mp3", "book a/track2.mp3"], ["book b/new.m4b"], ["single.m4b"]],
            StoredGroups());
        Assert.Equal(id, GroupIdOf("book a/track1.mp3"));
        Assert.NotEqual(singleId, GroupIdOf("single.m4b"));
        using var connection = _connections.Open();
        var runs = connection.Query<(long Id, long ScanRunId)>("SELECT Id, ScanRunId FROM FileGroup").ToDictionary(r => r.Id, r => r.ScanRunId);
        Assert.Equal(first.Id, runs[id]);
        Assert.All(runs.Where(r => r.Key != id), r => Assert.Equal(second.Id, r.Value));
        Assert.Empty(_grouping.List().Unresolved);
    }

    [Fact]
    public void Reordered_group_keeps_its_order_after_a_rescan_of_unchanged_files()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var id = GroupIdOf("book a/track1.mp3");
        var ids = _grouping.GetDetail(id)!.Files.Select(f => f.FileId).Reverse().ToList();

        var reordered = _grouping.Reorder(id, new ReorderGroupDto(ids));
        Assert.Empty(reordered.Errors);
        Assert.True(reordered.Group!.UserCorrected);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);

        Assert.Contains(["book a/track2.mp3", "book a/track1.mp3"], StoredGroups());
        Assert.Equal([1, 2], Positions("book a/track1.mp3"));
        Assert.Equal(ids, _grouping.GetDetail(id)!.Files.Select(f => f.FileId));
    }

    /// <summary>The stored file id of <paramref name="relativePath"/>.</summary>
    private long FileIdOf(string relativePath)
    {
        using var connection = _connections.Open();
        return connection.ExecuteScalar<long>(
            "SELECT Id FROM AudioFile WHERE SourcePath = @path",
            new { path = Path.Combine(_incoming, relativePath.Replace('/', Path.DirectorySeparatorChar)) });
    }

    [Fact]
    public void Moving_a_file_appends_it_renumbers_the_source_recomputes_totals_and_survives_a_rescan()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var bookId = GroupIdOf("book a/track1.mp3");
        var targetId = GroupIdOf("single.m4b");
        var single = _grouping.GetDetail(targetId)!;
        var track1 = _grouping.GetDetail(bookId)!.Files[0];

        var moved = _grouping.MoveFiles(targetId, new MoveFilesDto([track1.FileId]));

        Assert.Empty(moved.Errors);
        var target = moved.Group!;
        Assert.True(target.UserCorrected);
        Assert.Equal(["single.m4b", "book a/track1.mp3"], target.Files.Select(f => f.RelativePath));
        Assert.Equal([1, 2], target.Files.Select(f => f.Position));
        Assert.Equal(2, target.FileCount);
        Assert.Equal(single.TotalDurationMs + track1.DurationMs, target.TotalDurationMs);

        var book = _grouping.GetDetail(bookId)!;
        Assert.True(book.UserCorrected);
        Assert.Equal(["book a/track2.mp3"], book.Files.Select(f => f.RelativePath));
        Assert.Equal([1], book.Files.Select(f => f.Position));
        Assert.Equal(1, book.FileCount);
        Assert.Equal(book.Files[0].DurationMs, book.TotalDurationMs);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);

        Assert.Equal(target, _grouping.GetDetail(targetId), new DetailComparer());
        Assert.Equal(book, _grouping.GetDetail(bookId), new DetailComparer());
        Assert.Equal(2, _grouping.List().Groups.Count);
        Assert.Empty(_grouping.List().Unresolved);
    }

    [Fact]
    public void Merging_deletes_the_emptied_group()
    {
        NewScanService(_grouping).Run(CancellationToken.None);
        var bookId = GroupIdOf("book a/track1.mp3");
        var targetId = GroupIdOf("single.m4b");
        var ids = _grouping.GetDetail(bookId)!.Files.Select(f => f.FileId).ToList();

        var merged = _grouping.MoveFiles(targetId, new MoveFilesDto(ids));

        Assert.Empty(merged.Errors);
        Assert.Equal(3, merged.Group!.FileCount);
        Assert.Equal([1, 2, 3], merged.Group.Files.Select(f => f.Position));
        Assert.Null(_grouping.GetDetail(bookId));
        Assert.Equal(targetId, Assert.Single(_grouping.List().Groups).Id);
    }

    [Fact]
    public void Creating_a_group_takes_files_in_order_and_deletes_emptied_groups()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var bookId = GroupIdOf("book a/track1.mp3");
        var singleId = GroupIdOf("single.m4b");
        var ids = new[] { FileIdOf("single.m4b"), FileIdOf("book a/track2.mp3") };

        var created = _grouping.CreateGroup(new CreateGroupDto(ids, "  Split Book ", " ")).Group!;

        Assert.Equal(("Split Book", null, "High", true), (created.Title, created.Author, created.Confidence, created.UserCorrected));
        Assert.Equal(["Grouped by the user"], created.Evidence);
        Assert.Equal(["single.m4b", "book a/track2.mp3"], created.Files.Select(f => f.RelativePath));
        Assert.Equal([1, 2], created.Files.Select(f => f.Position));
        Assert.Equal(created.Files.Sum(f => f.DurationMs), created.TotalDurationMs);
        Assert.Null(_grouping.GetDetail(singleId));
        var book = _grouping.GetDetail(bookId)!;
        Assert.Equal(["book a/track1.mp3"], book.Files.Select(f => f.RelativePath));
        Assert.True(book.UserCorrected);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);
        Assert.Equal(created, _grouping.GetDetail(created.Id), new DetailComparer());
    }

    [Fact]
    public void Excluding_a_file_renumbers_its_book_lists_it_as_excluded_and_survives_a_rescan()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var bookId = GroupIdOf("book a/track1.mp3");
        var track1 = FileIdOf("book a/track1.mp3");

        var result = _grouping.Exclude(new MoveFilesDto([track1]));

        Assert.Empty(result.Errors);
        var book = _grouping.GetDetail(bookId)!;
        Assert.True(book.UserCorrected);
        Assert.Equal(["book a/track2.mp3"], book.Files.Select(f => f.RelativePath));
        Assert.Equal([1], book.Files.Select(f => f.Position));
        Assert.Equal(1, book.FileCount);
        Assert.Equal(book.Files[0].DurationMs, book.TotalDurationMs);
        var list = result.List!;
        var excluded = Assert.Single(list.Excluded);
        Assert.Equal((track1, "book a/track1.mp3"), (excluded.FileId, excluded.RelativePath));
        Assert.InRange(excluded.DurationMs!.Value, 29_500, 30_500);
        Assert.Empty(list.Unresolved);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);

        Assert.Equal(book, _grouping.GetDetail(bookId), new DetailComparer());
        var after = _grouping.List();
        Assert.Equal([track1], after.Excluded.Select(f => f.FileId));
        Assert.Empty(after.Unresolved);
        Assert.Equal(2, after.Groups.Count);
    }

    [Fact]
    public void An_unresolved_file_stays_unresolved_after_a_rescan_until_it_is_moved_into_a_group()
    {
        var scanner = NewScanService(_grouping);
        scanner.Run(CancellationToken.None);
        var track2 = FileIdOf("book a/track2.mp3");

        var result = _grouping.Unresolve(new MoveFilesDto([track2]));

        Assert.Empty(result.Errors);
        Assert.Equal([track2], result.List!.Unresolved.Select(f => f.FileId));
        Assert.Empty(result.List.Excluded);

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);
        Assert.Equal([track2], _grouping.List().Unresolved.Select(f => f.FileId));
        Assert.DoesNotContain(StoredGroups(), g => g.Contains("book a/track2.mp3"));

        // The rescan replaced the uncorrected M4B group, so look its id up again.
        var singleId = GroupIdOf("single.m4b");
        Assert.NotNull(_grouping.MoveFiles(singleId, new MoveFilesDto([track2])).Group);
        Assert.Null(UserStateOf(track2));

        Assert.Equal("Completed", scanner.Run(CancellationToken.None).Status);
        Assert.Equal(singleId, GroupIdOf("book a/track2.mp3"));
        Assert.Empty(_grouping.List().Unresolved);
    }

    [Fact]
    public void Excluding_the_only_file_deletes_its_group_and_unresolving_it_undoes_the_exclude()
    {
        NewScanService(_grouping).Run(CancellationToken.None);
        var singleId = GroupIdOf("single.m4b");
        var single = FileIdOf("single.m4b");

        var excluded = _grouping.Exclude(new MoveFilesDto([single]));

        Assert.Empty(excluded.Errors);
        Assert.Null(_grouping.GetDetail(singleId));
        Assert.Equal([single], excluded.List!.Excluded.Select(f => f.FileId));
        Assert.Single(excluded.List.Groups);

        var undone = _grouping.Unresolve(new MoveFilesDto([single]));

        Assert.Empty(undone.Errors);
        Assert.Empty(undone.List!.Excluded);
        Assert.Equal([single], undone.List.Unresolved.Select(f => f.FileId));
        Assert.Equal("Unresolved", UserStateOf(single));
    }

    [Fact]
    public void Creating_a_group_clears_the_files_user_state()
    {
        NewScanService(_grouping).Run(CancellationToken.None);
        var single = FileIdOf("single.m4b");
        Assert.Empty(_grouping.Exclude(new MoveFilesDto([single])).Errors);

        Assert.NotNull(_grouping.CreateGroup(new CreateGroupDto([single], null, null)).Group);

        Assert.Null(UserStateOf(single));
        Assert.Empty(_grouping.List().Excluded);
    }

    private string? UserStateOf(long fileId)
    {
        using var connection = _connections.Open();
        return connection.ExecuteScalar<string?>("SELECT UserState FROM AudioFile WHERE Id = @fileId", new { fileId });
    }

    /// <summary>Compares details by value, including their file lists.</summary>
    private sealed class DetailComparer : IEqualityComparer<GroupDetailDto?>
    {
        public bool Equals(GroupDetailDto? x, GroupDetailDto? y) =>
            x is not null && y is not null
            && x with { Evidence = [], Files = [] } == y with { Evidence = [], Files = [] }
            && x.Evidence.SequenceEqual(y.Evidence) && x.Files.SequenceEqual(y.Files);

        public int GetHashCode(GroupDetailDto? obj) => obj?.Id.GetHashCode() ?? 0;
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
