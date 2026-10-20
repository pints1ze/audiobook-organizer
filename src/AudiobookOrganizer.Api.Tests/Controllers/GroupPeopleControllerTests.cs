using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupPeopleControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private static object AudibleCandidate() => new
    {
        provider = "audible",
        asin = "B0TEST0001",
        title = "The Test Book",
        authors = new[] { "Ann Author" },
        narrators = new[] { "Ned Narrator", "Nia Narrator" },
        format = "unabridged",
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

    private static async Task<JsonElement> People(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}/people");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ScansControllerTests.ReadJson(response);
        Assert.Equal(id, body.GetProperty("groupId").GetInt64());
        return body;
    }

    private static async Task<long> CreatePerson(HttpClient client, string name, params string[] aliases)
    {
        var response = await client.PostAsJsonAsync("/api/people", new { name, aliases });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await ScansControllerTests.ReadJson(response)).GetProperty("id").GetInt64();
    }

    private static string? EntryName(JsonElement row) =>
        row.GetProperty("entry").ValueKind == JsonValueKind.Null ? null : row.GetProperty("entry").GetProperty("name").GetString();

    [Fact]
    public async Task Unknown_group_returns_404()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/people")).StatusCode);
    }

    [Fact]
    public async Task A_group_with_no_match_returns_its_file_fallback_author_resolved_by_a_variant()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        var rename = await client.PutAsJsonAsync($"/api/groups/{id}", new { title = "Some Book", author = "king,  stephen" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        await CreatePerson(client, "Stephen King", "Richard Bachman");

        var body = await People(client, id);

        var author = Assert.Single(body.GetProperty("authors").EnumerateArray());
        Assert.Equal("king,  stephen", author.GetProperty("name").GetString());
        Assert.Equal("Stephen King", EntryName(author));
        Assert.Equal(["Richard Bachman"], author.GetProperty("entry").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
        Assert.Empty(author.GetProperty("suggestions").EnumerateArray());
        Assert.Empty(body.GetProperty("narrators").EnumerateArray());
    }

    [Fact]
    public async Task Picked_names_resolve_through_aliases_and_unresolved_names_list_suggestions_without_linking()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate())).StatusCode);
        await CreatePerson(client, "Ann Author", "Author, Ann");
        await CreatePerson(client, "N. Narrator");

        var body = await People(client, id);

        var author = Assert.Single(body.GetProperty("authors").EnumerateArray());
        Assert.Equal("Ann Author", EntryName(author));
        Assert.Empty(author.GetProperty("suggestions").EnumerateArray());
        var narrators = body.GetProperty("narrators").EnumerateArray().ToList();
        Assert.Equal(["Ned Narrator", "Nia Narrator"], narrators.Select(n => n.GetProperty("name").GetString()));
        foreach (var narrator in narrators)
        {
            Assert.Null(EntryName(narrator));
            var suggestion = Assert.Single(narrator.GetProperty("suggestions").EnumerateArray());
            Assert.Equal("N. Narrator", suggestion.GetProperty("entry").GetProperty("name").GetString());
            Assert.Equal($"\"N. Narrator\" may be \"{narrator.GetProperty("name").GetString()}\" with initials.",
                suggestion.GetProperty("reason").GetString());
        }

        var people = await ScansControllerTests.ReadJson(await client.GetAsync("/api/people"));
        Assert.Equal(2, people.GetArrayLength());
    }

    [Fact]
    public async Task Adding_an_alias_resolves_the_name_and_renaming_the_person_changes_the_entry()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/match", AudibleCandidate())).StatusCode);
        var personId = await CreatePerson(client, "Edward Narrator");

        var before = await People(client, id);
        Assert.Null(EntryName(before.GetProperty("narrators")[0]));

        var addAlias = await client.PutAsJsonAsync($"/api/people/{personId}",
            new { name = "Edward Narrator", aliases = new[] { "Ned Narrator" } });
        Assert.Equal(HttpStatusCode.OK, addAlias.StatusCode);
        var linked = await People(client, id);
        Assert.Equal("Edward Narrator", EntryName(linked.GetProperty("narrators")[0]));
        Assert.Empty(linked.GetProperty("narrators")[0].GetProperty("suggestions").EnumerateArray());
        Assert.Null(EntryName(linked.GetProperty("narrators")[1]));

        var rename = await client.PutAsJsonAsync($"/api/people/{personId}",
            new { name = "Ed Narrator", aliases = new[] { "Ned Narrator" } });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var renamed = await People(client, id);
        Assert.Equal("Ed Narrator", EntryName(renamed.GetProperty("narrators")[0]));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
