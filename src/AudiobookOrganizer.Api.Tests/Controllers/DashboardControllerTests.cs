using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Tests.Services;

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

    [Fact]
    public async Task On_a_fresh_database_group_and_file_state_counts_are_zero()
    {
        var body = await ScansControllerTests.ReadJson(await _factory.CreateClient().GetAsync("/api/dashboard"));

        Assert.Equal(0, body.GetProperty("bookCount").GetInt32());
        var byConfidence = body.GetProperty("booksByConfidence");
        Assert.Equal(0, byConfidence.GetProperty("low").GetInt32());
        Assert.Equal(0, byConfidence.GetProperty("medium").GetInt32());
        Assert.Equal(0, byConfidence.GetProperty("high").GetInt32());
        Assert.Equal(0, body.GetProperty("correctedBookCount").GetInt32());
        Assert.Equal(0, body.GetProperty("unresolvedFileCount").GetInt32());
        Assert.Equal(0, body.GetProperty("excludedFileCount").GetInt32());
    }

    [Fact]
    public async Task After_a_scan_book_and_unresolved_counts_match_the_groups_list()
    {
        var client = _factory.CreateClient();
        await ScanMixedIncoming(client);

        var groups = await GetGroups(client);
        var body = await ScansControllerTests.ReadJson(await client.GetAsync("/api/dashboard"));

        AssertCountsMatch(groups, body);
        Assert.True(body.GetProperty("bookCount").GetInt32() >= 2);
        Assert.Equal(2, body.GetProperty("unresolvedFileCount").GetInt32());
        Assert.Equal(0, body.GetProperty("correctedBookCount").GetInt32());
        Assert.Equal(0, body.GetProperty("excludedFileCount").GetInt32());
    }

    [Fact]
    public async Task Counts_follow_a_rename_and_an_exclude()
    {
        var client = _factory.CreateClient();
        await ScanMixedIncoming(client);
        var before = await GetGroups(client);
        var unresolvedBefore = before.GetProperty("unresolved").GetArrayLength();
        var groupId = before.GetProperty("groups")[0].GetProperty("id").GetInt64();
        var fileId = before.GetProperty("unresolved")[0].GetProperty("fileId").GetInt64();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/groups/{groupId}", new { title = "Renamed", author = "Someone" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/groups/excluded", new { fileIds = new[] { fileId } })).StatusCode);

        var groups = await GetGroups(client);
        var body = await ScansControllerTests.ReadJson(await client.GetAsync("/api/dashboard"));

        AssertCountsMatch(groups, body);
        Assert.Equal(
            groups.GetProperty("groups").EnumerateArray().Count(g => g.GetProperty("userCorrected").GetBoolean()),
            body.GetProperty("correctedBookCount").GetInt32());
        Assert.Equal(1, body.GetProperty("correctedBookCount").GetInt32());
        Assert.Equal(1, body.GetProperty("excludedFileCount").GetInt32());
        Assert.Equal(unresolvedBefore - 1, body.GetProperty("unresolvedFileCount").GetInt32());
    }

    /// <summary>Two untagged root MP3s (unresolved), a folder with repeated track numbers (Low) and the M4B fixture.</summary>
    private async Task ScanMixedIncoming(HttpClient client)
    {
        var repeat = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "repeat")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(repeat, "01 a.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(repeat, "01 b.mp3"));
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(_folders.Incoming, "alpha.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_folders.Incoming, "beta.mp3"));
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());
    }

    private static async Task<JsonElement> GetGroups(HttpClient client) =>
        await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));

    private static void AssertCountsMatch(JsonElement groups, JsonElement dashboard)
    {
        var list = groups.GetProperty("groups").EnumerateArray().ToList();
        int Count(string confidence) => list.Count(g => g.GetProperty("confidence").GetString() == confidence);
        var byConfidence = dashboard.GetProperty("booksByConfidence");
        Assert.Equal(list.Count, dashboard.GetProperty("bookCount").GetInt32());
        Assert.Equal(Count("Low"), byConfidence.GetProperty("low").GetInt32());
        Assert.Equal(Count("Medium"), byConfidence.GetProperty("medium").GetInt32());
        Assert.Equal(Count("High"), byConfidence.GetProperty("high").GetInt32());
        Assert.Equal(groups.GetProperty("unresolved").GetArrayLength(), dashboard.GetProperty("unresolvedFileCount").GetInt32());
        Assert.Equal(groups.GetProperty("excluded").GetArrayLength(), dashboard.GetProperty("excludedFileCount").GetInt32());
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
