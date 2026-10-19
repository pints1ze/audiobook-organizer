using System.Net;
using System.Text.Json;
using AudiobookOrganizer.Api.Tests.Services;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupsControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private static async Task<JsonElement> GetGroups(HttpClient client)
    {
        var response = await client.GetAsync("/api/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private async Task Scan(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Fresh_database_returns_empty_lists_then_a_scan_returns_groups_and_unresolved_files()
    {
        var client = _factory.CreateClient();

        var empty = await GetGroups(client);
        Assert.Equal(0, empty.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, empty.GetProperty("unresolved").GetArrayLength());

        // Two untagged (no album) root MP3s with different names, plus the M4B fixture.
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(_folders.Incoming, "alpha.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_folders.Incoming, "beta.mp3"));
        await Scan(client);

        var result = await GetGroups(client);

        var group = Assert.Single(result.GetProperty("groups").EnumerateArray());
        Assert.True(group.GetProperty("id").GetInt64() > 0);
        Assert.Contains(group.GetProperty("confidence").GetString(), new[] { "High", "Medium" });
        Assert.Equal(1, group.GetProperty("fileCount").GetInt32());
        Assert.InRange(group.GetProperty("totalDurationMs").GetInt64(), 29_500, 30_500);
        Assert.True(group.GetProperty("evidence").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.String, group.GetProperty("evidence")[0].ValueKind);
        Assert.Equal("Fixture Author", group.GetProperty("author").GetString());
        Assert.Equal("chapters", group.GetProperty("title").GetString());

        var unresolved = result.GetProperty("unresolved").EnumerateArray().ToList();
        Assert.Equal(["alpha.mp3", "beta.mp3"],
            unresolved.Select(u => u.GetProperty("relativePath").GetString()).Order(StringComparer.Ordinal));
        Assert.All(unresolved, u =>
        {
            Assert.True(u.GetProperty("fileId").GetInt64() > 0);
            Assert.InRange(u.GetProperty("durationMs").GetInt64(), 29_500, 30_500);
        });
    }

    [Fact]
    public async Task Groups_needing_review_come_first()
    {
        var client = _factory.CreateClient();
        // Repeated track numbers in one folder with no disc info make a Low group; the lone M4B is Medium.
        var repeat = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "repeat")).FullName;
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(repeat, "01 a.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(repeat, "01 b.mp3"));
        await Scan(client);

        var groups = (await GetGroups(client)).GetProperty("groups").EnumerateArray().ToList();

        Assert.Equal(["Low", "Medium"], groups.Select(g => g.GetProperty("confidence").GetString()));
        Assert.Equal([2, 1], groups.Select(g => g.GetProperty("fileCount").GetInt32()));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
