using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupMatchControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private static object AudibleCandidate(string asin = "B0TEST0001", string title = "The Test Book") => new
    {
        provider = "audible",
        asin,
        title,
        subtitle = "A Novel",
        authors = new[] { "Ann Author" },
        narrators = new[] { "Ned Narrator", "Nia Narrator" },
        series = new { name = "Test Series", part = "1" },
        publisher = "Test House",
        releaseDate = "2020-01-02",
        language = "english",
        runtimeMinutes = 600,
        format = "unabridged",
        coverUrl = "https://m.media-amazon.com/images/I/test.jpg",
        isbn = (string?)null,
    };

    private async Task Scan(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
    }

    /// <summary>Scans the default folder (the M4B fixture plus an MP3) and returns the first group's id.</summary>
    private async Task<long> ScanAndTakeGroup(HttpClient client)
    {
        await Scan(client);
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return list.GetProperty("groups")[0].GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> GetGroup(HttpClient client, long id) =>
        await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}"));

    private long Count(string table)
    {
        using var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open();
        return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
    }

    [Fact]
    public async Task A_fresh_book_has_no_match_then_put_stores_the_candidate_and_get_returns_it()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/groups/{id}/match")).StatusCode);

        var put = await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var picked = await ScansControllerTests.ReadJson(put);
        Assert.Equal(id, picked.GetProperty("groupId").GetInt64());
        var match = picked.GetProperty("match");
        Assert.Equal("audible", match.GetProperty("provider").GetString());
        Assert.Equal("B0TEST0001", match.GetProperty("asin").GetString());
        Assert.Equal("The Test Book", match.GetProperty("title").GetString());
        Assert.Equal(["Ned Narrator", "Nia Narrator"],
            match.GetProperty("narrators").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("Test Series", match.GetProperty("series").GetProperty("name").GetString());
        Assert.Equal(600, match.GetProperty("runtimeMinutes").GetInt32());
        Assert.True(picked.GetProperty("pickedAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));

        var get = await client.GetAsync($"/api/groups/{id}/match");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(picked.GetRawText(), (await ScansControllerTests.ReadJson(get)).GetRawText());
    }

    [Fact]
    public async Task Picking_a_second_candidate_replaces_the_first_and_marks_the_group_corrected()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        Assert.False((await GetGroup(client, id)).GetProperty("userCorrected").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate())).StatusCode);
        var second = await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate("B0TEST0002", "  Second Book  "));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var match = (await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}/match"))).GetProperty("match");
        Assert.Equal("B0TEST0002", match.GetProperty("asin").GetString());
        Assert.Equal("Second Book", match.GetProperty("title").GetString());
        Assert.Equal(1, Count("Audiobook"));
        Assert.Equal(1, Count("MetadataMatch"));
        Assert.True((await GetGroup(client, id)).GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task Unknown_groups_404_and_bad_candidates_return_400_with_the_field_and_store_nothing()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/match")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync("/api/groups/999999/match", AudibleCandidate())).StatusCode);

        var tooMany = Enumerable.Range(1, 51).Select(i => $"Narrator {i}").ToArray();
        (object Body, string Field)[] bad =
        [
            (new { provider = "goodreads", title = "x", authors = Array.Empty<string>(), narrators = Array.Empty<string>() }, "provider"),
            (new { provider = "audible", title = "   ", authors = Array.Empty<string>(), narrators = Array.Empty<string>() }, "title"),
            (new { provider = "audible", title = new string('a', 501), authors = Array.Empty<string>(), narrators = Array.Empty<string>() }, "title"),
            (new { provider = "audible", title = "x", authors = new[] { new string('a', 501) }, narrators = Array.Empty<string>() }, "authors"),
            (new { provider = "audible", title = "x", authors = Array.Empty<string>(), narrators = tooMany }, "narrators"),
            (new { provider = "audible", title = "x", authors = Array.Empty<string>(), narrators = Array.Empty<string>(), coverUrl = "ftp://example.com/c.jpg" }, "coverUrl"),
            (new { provider = "audible", title = "x", authors = Array.Empty<string>(), narrators = Array.Empty<string>(), coverUrl = "/relative.jpg" }, "coverUrl"),
            (new { provider = "audible", title = "x", authors = Array.Empty<string>(), narrators = Array.Empty<string>(), runtimeMinutes = -1 }, "runtimeMinutes"),
            (new { provider = "openlibrary", title = "x", authors = Array.Empty<string>(), narrators = Array.Empty<string>(), series = new { name = new string('a', 501), part = "1" } }, "series.name"),
        ];
        foreach (var (body, field) in bad)
        {
            var response = await client.PutAsJsonAsync($"/api/groups/{id}/match", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        }

        Assert.Equal(0, Count("Audiobook"));
        Assert.Equal(0, Count("MetadataMatch"));
        Assert.False((await GetGroup(client, id)).GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task A_rescan_of_the_unchanged_folder_keeps_the_group_and_its_match()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        var picked = await ScansControllerTests.ReadJson(
            await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate()));

        await Scan(client);

        var ids = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups")))
            .GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64());
        Assert.Contains(id, ids);
        var get = await client.GetAsync($"/api/groups/{id}/match");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(picked.GetRawText(), (await ScansControllerTests.ReadJson(get)).GetRawText());
    }

    [Fact]
    public async Task Merging_the_matched_group_into_another_removes_its_audiobook_and_match()
    {
        var client = _factory.CreateClient();
        // Two folders, one book each.
        foreach (var name in new[] { "one", "two" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(_folders.Incoming, name)).FullName;
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(folder, "track1.mp3"));
        }
        await Scan(client);
        var groups = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups")))
            .GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64()).ToList();
        var (matched, other) = (groups[0], groups[1]);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/groups/{matched}/match", AudibleCandidate())).StatusCode);
        Assert.Equal(1, Count("Audiobook"));

        var fileIds = (await GetGroup(client, matched)).GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("fileId").GetInt64()).ToList();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/groups/{other}/files", new { fileIds })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{matched}/match")).StatusCode);
        Assert.Equal(0, Count("Audiobook"));
        Assert.Equal(0, Count("MetadataMatch"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
