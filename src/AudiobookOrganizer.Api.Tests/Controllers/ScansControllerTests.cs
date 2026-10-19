using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Tests.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class ScansControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    [Fact]
    public async Task Post_returns_202_running_and_the_scan_completes_in_the_background()
    {
        var client = _factory.CreateClient();
        await _folders.SaveSettings(client);

        var response = await client.PostAsync("/api/scans", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/scans/latest", response.Headers.Location?.OriginalString);
        var started = await ReadJson(response);
        Assert.Equal("Running", started.GetProperty("status").GetString());
        Assert.True(started.GetProperty("id").GetInt64() > 0);

        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(2, done.GetProperty("filesFound").GetInt32());
        Assert.Equal(2, done.GetProperty("filesAdded").GetInt32());
        Assert.Equal(started.GetProperty("id").GetInt64(), done.GetProperty("id").GetInt64());
        Assert.NotNull(done.GetProperty("finishedAt").GetString());
    }

    [Fact]
    public async Task Second_post_while_a_scan_runs_returns_409_with_a_message()
    {
        var reader = new GatedAudioFileReader(blockAfter: 0);
        using var gated = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<AudioFileReader>(reader)));
        var client = gated.CreateClient();
        await _folders.SaveSettings(client);

        try
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
            reader.WaitUntilBlocked();

            var second = await client.PostAsync("/api/scans", null);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var problem = await ReadJson(second);
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
            Assert.Equal(409, problem.GetProperty("status").GetInt32());
        }
        finally
        {
            reader.Release();
        }

        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        await ScanTestFolders.WaitForFinish(client);
    }

    [Fact]
    public async Task Latest_on_a_fresh_database_returns_204()
    {
        var response = await _factory.CreateClient().GetAsync("/api/scans/latest");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_run_left_running_is_failed_when_the_app_restarts()
    {
        var interrupted = _factory.Services.GetRequiredService<ScanRepository>().InsertRun(DateTime.UtcNow.ToString("O"));

        // A second host over the same database file runs startup again.
        using var restarted = _factory.WithWebHostBuilder(_ => { });
        var latest = await ReadJson(await restarted.CreateClient().GetAsync("/api/scans/latest"));

        Assert.Equal(interrupted.Id, latest.GetProperty("id").GetInt64());
        Assert.Equal("Failed", latest.GetProperty("status").GetString());
        Assert.Equal("Interrupted by an application restart.", latest.GetProperty("error").GetString());
        Assert.NotNull(latest.GetProperty("finishedAt").GetString());
    }

    internal static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}

/// <summary>Temp incoming and library folders, with both audio fixtures copied into incoming.</summary>
internal sealed class ScanTestFolders : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-scan-api-{Guid.NewGuid():N}");

    public ScanTestFolders()
    {
        Incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        Library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), Path.Combine(Incoming, "chapters.m4b"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(Incoming, "chapters.mp3"));
    }

    public string Incoming { get; }
    public string Library { get; }

    public async Task SaveSettings(HttpClient client)
    {
        var response = await client.PutAsJsonAsync("/api/settings",
            new { incomingPath = Incoming, libraryPath = Library, keepSourceFiles = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Polls GET /api/scans/latest until the run isn't Running, failing after 10 seconds.</summary>
    public static async Task<JsonElement> WaitForFinish(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var response = await client.GetAsync("/api/scans/latest");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var run = await ScansControllerTests.ReadJson(response);
            if (run.GetProperty("status").GetString() != "Running") return run;
            Assert.True(DateTime.UtcNow < deadline, "The scan didn't finish within 10 seconds.");
            await Task.Delay(50);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
