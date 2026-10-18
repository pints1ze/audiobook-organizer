using System.Security.Cryptography;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookOrganizer.Api.Tests.Services;

public class ScanServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-scan-{Guid.NewGuid():N}");

    private readonly string _incoming;
    private readonly string _disc1;
    private readonly string _library;
    private readonly SettingsService _settings;
    private readonly ScanRepository _repository;
    private readonly ScanService _service;

    public ScanServiceTests()
    {
        _incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        _disc1 = Directory.CreateDirectory(Path.Combine(_incoming, "disc 1")).FullName;
        _library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;

        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), Path.Combine(_disc1, "chapters.m4b"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_disc1, "chapters.mp3"));
        File.WriteAllText(Path.Combine(_disc1, "notes.txt"), "not audio");
        File.WriteAllBytes(Path.Combine(_disc1, "cover.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);

        var connections = new SqliteConnectionFactory(Path.Combine(_root, "scan.db"));
        new DatabaseInitializer(connections).Initialize();
        _settings = new SettingsService(new SettingsRepository(connections));
        _repository = new ScanRepository(connections);
        _service = new ScanService(_settings, _repository, AudioFileReaderTests.NewReader(),
            NullLogger<ScanService>.Instance);
    }

    private void ConfigureIncoming() =>
        Assert.Empty(_settings.Save(new SettingsDto(_incoming, _library, false)).Errors);

    private Dictionary<string, AudioFile> Rows() =>
        _repository.GetFilesUnder(_incoming).ToDictionary(f => f.FileName);

    [Fact]
    public void First_scan_inventories_nested_audio_and_ignores_other_files()
    {
        ConfigureIncoming();

        var run = _service.Run(CancellationToken.None);

        Assert.Equal("Completed", run.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.Null(run.Error);
        Assert.Equal((2, 2, 0, 0, 0, 0),
            (run.FilesFound, run.FilesAdded, run.FilesUpdated, run.FilesUnchanged, run.FilesMissing, run.FilesFailed));

        var rows = Rows();
        Assert.Equal(["chapters.m4b", "chapters.mp3"], rows.Keys.Order());
        foreach (var (name, ext) in new[] { ("chapters.m4b", "m4b"), ("chapters.mp3", "mp3") })
        {
            var row = rows[name];
            var path = Path.Combine(_disc1, name);
            Assert.Equal(path, row.SourcePath);
            Assert.Equal(ext, row.Extension);
            Assert.Equal(new FileInfo(path).Length, row.SizeBytes);
            Assert.Equal(File.GetLastWriteTimeUtc(path), DateTime.Parse(row.ModifiedAtUtc).ToUniversalTime());
            Assert.Equal("Fixture Book", row.Title);
            Assert.Equal("Fixture Author", row.Artist);
            Assert.InRange(row.DurationMs!.Value, 29_500, 30_500);
            Assert.Equal(3, row.ChapterCount);
            Assert.False(row.HasCover);
            Assert.Null(row.ScanError);
            Assert.Equal(run.Id, row.FirstSeenScanId);
            Assert.Equal(run.Id, row.LastSeenScanId);
        }
    }

    [Fact]
    public void Rescan_skips_unchanged_and_tracks_updated_added_and_missing()
    {
        ConfigureIncoming();
        var first = _service.Run(CancellationToken.None);
        var idsBefore = Rows().ToDictionary(r => r.Key, r => r.Value.Id);

        var second = _service.Run(CancellationToken.None);
        Assert.Equal((2, 0, 0, 2, 0, 0),
            (second.FilesFound, second.FilesAdded, second.FilesUpdated, second.FilesUnchanged, second.FilesMissing, second.FilesFailed));
        Assert.All(Rows().Values, r => Assert.Equal(second.Id, r.LastSeenScanId));
        Assert.All(Rows().Values, r => Assert.Equal(first.Id, r.FirstSeenScanId));

        var mp3 = Path.Combine(_disc1, "chapters.mp3");
        File.SetLastWriteTimeUtc(mp3, File.GetLastWriteTimeUtc(mp3).AddMinutes(5));
        var touched = _service.Run(CancellationToken.None);
        Assert.Equal((1, 1), (touched.FilesUpdated, touched.FilesUnchanged));
        Assert.Equal(idsBefore["chapters.mp3"], Rows()["chapters.mp3"].Id);
        Assert.Equal(File.GetLastWriteTimeUtc(mp3), DateTime.Parse(Rows()["chapters.mp3"].ModifiedAtUtc).ToUniversalTime());

        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_incoming, "THIRD.MP3"));
        var added = _service.Run(CancellationToken.None);
        Assert.Equal((3, 1, 0, 2), (added.FilesFound, added.FilesAdded, added.FilesUpdated, added.FilesUnchanged));

        File.Delete(Path.Combine(_disc1, "chapters.m4b"));
        var missing = _service.Run(CancellationToken.None);
        Assert.Equal((2, 0, 0, 2, 1),
            (missing.FilesFound, missing.FilesAdded, missing.FilesUpdated, missing.FilesUnchanged, missing.FilesMissing));
        Assert.True(Rows().ContainsKey("chapters.m4b"), "the missing file's row is kept");
        Assert.Equal("Completed", missing.Status);
    }

    [Fact]
    public void Corrupt_file_is_recorded_with_an_error_and_the_scan_completes()
    {
        ConfigureIncoming();
        File.WriteAllBytes(Path.Combine(_incoming, "broken.mp3"), RandomNumberGenerator.GetBytes(4096));

        var run = _service.Run(CancellationToken.None);

        Assert.Equal("Completed", run.Status);
        Assert.Equal((3, 2, 1), (run.FilesFound, run.FilesAdded, run.FilesFailed));
        var broken = Rows()["broken.mp3"];
        Assert.False(string.IsNullOrWhiteSpace(broken.ScanError));
        Assert.Equal(4096, broken.SizeBytes);
        Assert.Equal("mp3", broken.Extension);
        Assert.Null(broken.Title);
        Assert.Null(Rows()["chapters.mp3"].ScanError);
    }

    [Fact]
    public void Scan_leaves_every_incoming_file_byte_and_timestamp_identical()
    {
        ConfigureIncoming();
        File.WriteAllBytes(Path.Combine(_incoming, "broken.mp3"), RandomNumberGenerator.GetBytes(4096));
        var before = Snapshot(_incoming);

        _service.Run(CancellationToken.None);
        _service.Run(CancellationToken.None);

        Assert.Equal(before, Snapshot(_incoming));
    }

    [Fact]
    public void Unconfigured_incoming_fails_the_run_with_a_message()
    {
        var run = _service.Run(CancellationToken.None);

        Assert.Equal("Failed", run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.Error));
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(run.Status, _repository.GetRun(run.Id)!.Status);
    }

    [Fact]
    public void Incoming_that_no_longer_exists_fails_the_run_with_a_message()
    {
        ConfigureIncoming();
        Directory.Delete(_incoming, recursive: true);

        var run = _service.Run(CancellationToken.None);

        Assert.Equal("Failed", run.Status);
        Assert.Contains("doesn't exist", run.Error);
    }

    [Fact]
    public void App_registers_the_scan_service_and_creates_its_tables()
    {
        using var factory = new AudiobookOrganizerWebApplicationFactory();
        var service = factory.Services.GetRequiredService<ScanService>();

        var run = service.Run(CancellationToken.None); // no incoming configured in a fresh test database

        Assert.Equal("Failed", run.Status);
        Assert.Empty(factory.Services.GetRequiredService<ScanRepository>().GetFilesUnder(_incoming));
    }

    private static string[] Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(p => $"{p}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}|{File.GetLastWriteTimeUtc(p).Ticks}")
            .Order()
            .ToArray();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
