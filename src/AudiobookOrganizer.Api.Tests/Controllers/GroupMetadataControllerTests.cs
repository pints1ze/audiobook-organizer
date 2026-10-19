using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupMetadataControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private static readonly string[] FieldNames =
    [
        "title", "subtitle", "authors", "narrators", "series", "seriesPart",
        "publisher", "releaseDate", "language", "format", "asin", "isbn",
    ];

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

    private async Task<long> ScanAndTakeGroup(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return list.GetProperty("groups")[0].GetProperty("id").GetInt64();
    }

    private static async Task<JsonElement> Fields(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}/metadata");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ScansControllerTests.ReadJson(response);
        Assert.Equal(id, body.GetProperty("groupId").GetInt64());
        return body.GetProperty("fields");
    }

    private static async Task Pick(HttpClient client, long id, object candidate) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/match", candidate)).StatusCode);

    private static string? Text(JsonElement fields, string name) =>
        fields.GetProperty(name).GetProperty("value").GetString();

    private static string[] List(JsonElement fields, string name) =>
        fields.GetProperty(name).GetProperty("value").EnumerateArray().Select(v => v.GetString()!).ToArray();

    private static (string? Origin, string? Provider) Source(JsonElement fields, string name)
    {
        var field = fields.GetProperty(name);
        return (field.GetProperty("origin").GetString(), field.GetProperty("provider").GetString());
    }

    private long Count(string table)
    {
        using var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open();
        return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
    }

    [Fact]
    public async Task A_fresh_book_falls_back_to_the_group_title_and_author_and_unknown_groups_404()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        var group = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}"));
        var groupTitle = group.GetProperty("title").GetString();
        var groupAuthor = group.GetProperty("author").GetString();
        Assert.NotNull(groupTitle);

        var fields = await Fields(client, id);

        Assert.Equal(FieldNames.Order(), fields.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(groupTitle, Text(fields, "title"));
        Assert.Equal(("file", null), Source(fields, "title"));
        if (groupAuthor is null)
        {
            Assert.Empty(List(fields, "authors"));
            Assert.Equal((null, null), Source(fields, "authors"));
        }
        else
        {
            Assert.Equal([groupAuthor], List(fields, "authors"));
            Assert.Equal(("file", null), Source(fields, "authors"));
        }
        Assert.Empty(List(fields, "narrators"));
        Assert.Equal((null, null), Source(fields, "narrators"));
        foreach (var name in FieldNames.Except(["title", "authors", "narrators"]))
        {
            Assert.Equal(JsonValueKind.Null, fields.GetProperty(name).GetProperty("value").ValueKind);
            Assert.Equal((null, null), Source(fields, name));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/metadata")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/groups/999999/metadata",
            new { fields = new { title = "x" } })).StatusCode);
    }

    [Fact]
    public async Task Picking_an_Audible_candidate_fills_its_fields_and_a_later_gap_keeps_the_earlier_value()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        await Pick(client, id, AudibleCandidate());
        var fields = await Fields(client, id);

        Assert.Equal("The Test Book", Text(fields, "title"));
        Assert.Equal("A Novel", Text(fields, "subtitle"));
        Assert.Equal(["Ann Author"], List(fields, "authors"));
        Assert.Equal(["Ned Narrator", "Nia Narrator"], List(fields, "narrators"));
        Assert.Equal("Test Series", Text(fields, "series"));
        Assert.Equal("1", Text(fields, "seriesPart"));
        Assert.Equal("Test House", Text(fields, "publisher"));
        Assert.Equal("2020-01-02", Text(fields, "releaseDate"));
        Assert.Equal("english", Text(fields, "language"));
        Assert.Equal("unabridged", Text(fields, "format"));
        Assert.Equal("B0TEST0001", Text(fields, "asin"));
        foreach (var name in FieldNames.Except(["isbn"]))
            Assert.Equal(("provider", "audible"), Source(fields, name));
        Assert.Null(Text(fields, "isbn"));
        Assert.Equal((null, null), Source(fields, "isbn"));

        await Pick(client, id, new
        {
            provider = "audible", asin = "B0TEST0002", title = "Second Book", subtitle = "  ",
            authors = new[] { "Bea Author" }, narrators = Array.Empty<string>(), series = (object?)null,
        });
        fields = await Fields(client, id);

        Assert.Equal("Second Book", Text(fields, "title"));
        Assert.Equal("B0TEST0002", Text(fields, "asin"));
        Assert.Equal(["Bea Author"], List(fields, "authors"));
        Assert.Equal("A Novel", Text(fields, "subtitle"));
        Assert.Equal(["Ned Narrator", "Nia Narrator"], List(fields, "narrators"));
        Assert.Equal("Test Series", Text(fields, "series"));
        Assert.Equal("1", Text(fields, "seriesPart"));
        Assert.Equal("Test House", Text(fields, "publisher"));
    }

    [Fact]
    public async Task User_values_survive_a_later_pick()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        await Pick(client, id, AudibleCandidate());

        var put = await client.PutAsJsonAsync($"/api/groups/{id}/metadata",
            new { fields = new { title = "  My Title  ", narrators = new[] { " Una User ", " " } } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var returned = (await ScansControllerTests.ReadJson(put)).GetProperty("fields");
        Assert.Equal("My Title", Text(returned, "title"));
        Assert.Equal(["Una User"], List(returned, "narrators"));
        Assert.Equal(("user", null), Source(returned, "title"));
        Assert.Equal(("user", null), Source(returned, "narrators"));
        Assert.Equal("A Novel", Text(returned, "subtitle"));
        Assert.Equal(("provider", "audible"), Source(returned, "subtitle"));

        await Pick(client, id, new
        {
            provider = "audible", asin = "B0OTHER001", title = "Other Book", subtitle = "Other Subtitle",
            authors = new[] { "Oscar Other" }, narrators = new[] { "Olga Other" }, publisher = "Other House",
        });
        var fields = await Fields(client, id);

        Assert.Equal("My Title", Text(fields, "title"));
        Assert.Equal(["Una User"], List(fields, "narrators"));
        Assert.Equal(("user", null), Source(fields, "title"));
        Assert.Equal(("user", null), Source(fields, "narrators"));
        Assert.Equal("Other Subtitle", Text(fields, "subtitle"));
        Assert.Equal(["Oscar Other"], List(fields, "authors"));
        Assert.Equal("Other House", Text(fields, "publisher"));
        Assert.Equal("B0OTHER001", Text(fields, "asin"));
    }

    [Fact]
    public async Task A_blank_user_value_is_stored_as_an_explicit_empty_user_value()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        await Pick(client, id, AudibleCandidate());

        var put = await client.PutAsJsonAsync($"/api/groups/{id}/metadata",
            new { fields = new { subtitle = "  ", narrators = Array.Empty<string>() } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        await Pick(client, id, AudibleCandidate("B0TEST0003"));
        var fields = await Fields(client, id);

        Assert.Equal(JsonValueKind.Null, fields.GetProperty("subtitle").GetProperty("value").ValueKind);
        Assert.Equal(("user", null), Source(fields, "subtitle"));
        Assert.Empty(List(fields, "narrators"));
        Assert.Equal(("user", null), Source(fields, "narrators"));
        Assert.True((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}")))
            .GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task An_Open_Library_pick_fills_only_empty_or_file_values_after_an_Audible_pick()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        await Pick(client, id, new
        {
            provider = "audible", asin = "B0TEST0001", title = "The Test Book",
            authors = new[] { "Ann Author" }, narrators = new[] { "Ned Narrator" }, publisher = "Test House",
        });

        await Pick(client, id, new
        {
            provider = "openlibrary", title = "The Test Book (Print)", subtitle = "Print Subtitle",
            authors = new[] { "A. Author" }, narrators = Array.Empty<string>(), publisher = "Print House",
            releaseDate = "1999", isbn = "9780000000001",
        });
        var fields = await Fields(client, id);

        Assert.Equal("The Test Book", Text(fields, "title"));
        Assert.Equal(["Ann Author"], List(fields, "authors"));
        Assert.Equal("Test House", Text(fields, "publisher"));
        Assert.Equal(("provider", "audible"), Source(fields, "publisher"));
        Assert.Equal("Print Subtitle", Text(fields, "subtitle"));
        Assert.Equal(("provider", "openlibrary"), Source(fields, "subtitle"));
        Assert.Equal("1999", Text(fields, "releaseDate"));
        Assert.Equal("9780000000001", Text(fields, "isbn"));
        Assert.Equal(("provider", "openlibrary"), Source(fields, "isbn"));
    }

    [Fact]
    public async Task An_Open_Library_pick_replaces_the_file_fallback()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        await Pick(client, id, new
        {
            provider = "openlibrary", title = "Print Title", authors = new[] { "Print Author" },
            narrators = Array.Empty<string>(),
        });
        var fields = await Fields(client, id);

        Assert.Equal("Print Title", Text(fields, "title"));
        Assert.Equal(("provider", "openlibrary"), Source(fields, "title"));
        Assert.Equal(["Print Author"], List(fields, "authors"));
    }

    [Fact]
    public async Task Bad_edits_return_400_with_the_field_and_store_nothing()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        var blank = await client.PutAsJsonAsync($"/api/groups/{id}/metadata", new { fields = new { title = "  " } });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        var problem = await ScansControllerTests.ReadJson(blank);
        Assert.Equal("The title is required.", problem.GetProperty("errors").GetProperty("title")[0].GetString());

        var tooLong = await client.PutAsJsonAsync($"/api/groups/{id}/metadata",
            new { fields = new { publisher = new string('a', 501), subtitle = "fine" } });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        problem = await ScansControllerTests.ReadJson(tooLong);
        Assert.Equal("This value can be at most 500 characters.",
            problem.GetProperty("errors").GetProperty("publisher")[0].GetString());

        var tooMany = Enumerable.Range(1, 51).Select(i => $"Narrator {i}").ToArray();
        var many = await client.PutAsJsonAsync($"/api/groups/{id}/metadata", new { fields = new { narrators = tooMany } });
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
        problem = await ScansControllerTests.ReadJson(many);
        Assert.Equal("There can be at most 50 names.", problem.GetProperty("errors").GetProperty("narrators")[0].GetString());

        Assert.Equal(0, Count("Audiobook"));
        Assert.Equal(0, Count("AudiobookField"));
        Assert.False((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}")))
            .GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task Put_creates_the_audiobook_and_marks_the_group_corrected()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);

        var put = await client.PutAsJsonAsync($"/api/groups/{id}/metadata", new { fields = new { publisher = "Self" } });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(1, Count("Audiobook"));
        Assert.Equal(1, Count("AudiobookField"));
        Assert.True((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}")))
            .GetProperty("userCorrected").GetBoolean());
        var fields = await Fields(client, id);
        Assert.Equal("Self", Text(fields, "publisher"));
        Assert.Equal(("file", null), Source(fields, "title"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
