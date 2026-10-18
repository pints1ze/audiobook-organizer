using System.Net;
using System.Text.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class DashboardControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    [Fact]
    public async Task After_a_completed_scan_returns_paths_last_scan_and_counts()
    {
        var client = _factory.CreateClient();
        await _folders.SaveSettings(client);
        await client.PostAsync("/api/scans", null);
        await ScanTestFolders.WaitForFinish(client);

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ScansControllerTests.ReadJson(response);
        Assert.Equal(_folders.Incoming, body.GetProperty("incomingPath").GetString());
        Assert.Equal(_folders.Library, body.GetProperty("libraryPath").GetString());
        Assert.Equal("Completed", body.GetProperty("lastScan").GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("audioFileCount").GetInt32());
        Assert.Equal(0, body.GetProperty("filesWithErrors").GetInt32());
    }

    [Fact]
    public async Task Counts_only_files_seen_in_the_latest_completed_scan_and_their_errors()
    {
        var client = _factory.CreateClient();
        await _folders.SaveSettings(client);
        File.WriteAllBytes(Path.Combine(_folders.Incoming, "broken.mp3"), [1, 2, 3, 4]);
        await client.PostAsync("/api/scans", null);
        await ScanTestFolders.WaitForFinish(client);
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        await client.PostAsync("/api/scans", null);
        await ScanTestFolders.WaitForFinish(client);

        var body = await ScansControllerTests.ReadJson(await client.GetAsync("/api/dashboard"));

        Assert.Equal(2, body.GetProperty("audioFileCount").GetInt32());
        Assert.Equal(1, body.GetProperty("filesWithErrors").GetInt32());
    }

    [Fact]
    public async Task On_a_fresh_database_last_scan_is_null_and_counts_are_zero()
    {
        var body = await ScansControllerTests.ReadJson(await _factory.CreateClient().GetAsync("/api/dashboard"));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("incomingPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("libraryPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastScan").ValueKind);
        Assert.Equal(0, body.GetProperty("audioFileCount").GetInt32());
        Assert.Equal(0, body.GetProperty("filesWithErrors").GetInt32());
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
