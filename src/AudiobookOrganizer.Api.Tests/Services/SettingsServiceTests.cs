using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-settings-{Guid.NewGuid():N}");

    private readonly string _incoming;
    private readonly string _library;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        _library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
        File.WriteAllText(Path.Combine(_incoming, "book.mp3"), "not really audio");

        var connections = new SqliteConnectionFactory(Path.Combine(_root, "settings.db"));
        new DatabaseInitializer(connections).Initialize();
        _service = new SettingsService(new SettingsRepository(connections));
    }

    [Fact]
    public void Get_on_fresh_database_returns_nulls_and_false()
    {
        var settings = _service.Get();

        Assert.Equal(new SettingsDto(null, null, false), settings);
    }

    [Fact]
    public void Save_with_valid_directories_persists_and_leaves_no_trace()
    {
        var incomingBefore = Snapshot(_incoming);

        var result = _service.Save(new SettingsDto(_incoming, _library, true));

        Assert.Empty(result.Errors);
        Assert.Equal(new SettingsDto(_incoming, _library, true), result.Saved);
        Assert.Equal(new SettingsDto(_incoming, _library, true), _service.Get());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_library));
        Assert.Equal(incomingBefore, Snapshot(_incoming));
    }

    [Fact]
    public void Save_overwrites_previous_values()
    {
        var otherLibrary = Directory.CreateDirectory(Path.Combine(_root, "library2")).FullName;
        _service.Save(new SettingsDto(_incoming, _library, true));

        _service.Save(new SettingsDto(_incoming, otherLibrary, false));

        Assert.Equal(new SettingsDto(_incoming, otherLibrary, false), _service.Get());
    }

    public static TheoryData<string> InvalidPathCases => ["missing", "blank", "relative", "nonexistent", "file"];

    [Theory]
    [MemberData(nameof(InvalidPathCases))]
    public void Invalid_incoming_path_errors_on_incomingPath_only(string kind)
    {
        var result = _service.Save(new SettingsDto(InvalidPath(kind), _library, false));

        Assert.Null(result.Saved);
        Assert.True(result.Errors.ContainsKey("incomingPath"));
        Assert.False(result.Errors.ContainsKey("libraryPath"));
        Assert.Equal(new SettingsDto(null, null, false), _service.Get());
    }

    [Theory]
    [MemberData(nameof(InvalidPathCases))]
    public void Invalid_library_path_errors_on_libraryPath_only(string kind)
    {
        var result = _service.Save(new SettingsDto(_incoming, InvalidPath(kind), false));

        Assert.Null(result.Saved);
        Assert.True(result.Errors.ContainsKey("libraryPath"));
        Assert.False(result.Errors.ContainsKey("incomingPath"));
        Assert.Equal(new SettingsDto(null, null, false), _service.Get());
    }

    [Fact]
    public void Same_directory_for_both_is_rejected()
    {
        var result = _service.Save(new SettingsDto(_incoming, _incoming + Path.DirectorySeparatorChar, false));

        Assert.True(result.Errors.ContainsKey("libraryPath"));
        Assert.Null(result.Saved);
    }

    [Fact]
    public void Library_inside_incoming_is_rejected()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_incoming, "lib")).FullName;

        var result = _service.Save(new SettingsDto(_incoming, nested, false));

        Assert.True(result.Errors.ContainsKey("libraryPath"));
        Assert.Null(result.Saved);
    }

    [Fact]
    public void Incoming_inside_library_is_rejected()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_library, "in")).FullName;

        var result = _service.Save(new SettingsDto(nested, _library, false));

        Assert.True(result.Errors.ContainsKey("libraryPath"));
        Assert.Null(result.Saved);
    }

    [Fact]
    public void Sibling_with_shared_prefix_is_allowed()
    {
        var sibling = Directory.CreateDirectory(_incoming + "-library").FullName;

        var result = _service.Save(new SettingsDto(_incoming, sibling, false));

        Assert.Empty(result.Errors);
    }

    private string? InvalidPath(string kind) => kind switch
    {
        "missing" => null,
        "blank" => "   ",
        "relative" => Path.Combine("relative", "dir"),
        "nonexistent" => Path.Combine(_root, "does-not-exist"),
        "file" => CreateFile(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private string CreateFile()
    {
        var path = Path.Combine(_root, "plain-file.txt");
        File.WriteAllText(path, "x");
        return path;
    }

    private static string[] Snapshot(string dir) =>
        Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
            .Select(p => $"{p}|{(File.Exists(p) ? new FileInfo(p).Length + "|" + File.GetLastWriteTimeUtc(p).Ticks : "dir")}")
            .Order()
            .ToArray();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
