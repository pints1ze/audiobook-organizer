using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupGenresControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private async Task<long> ScanAndTakeGroup(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return list.GetProperty("groups")[0].GetProperty("id").GetInt64();
    }

    private static async Task SetGenres(HttpClient client, long id, params string[] genres)
    {
        var put = await client.PutAsJsonAsync($"/api/groups/{id}/metadata", new { fields = new { genres } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    private static async Task<List<JsonElement>> Genres(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}/genres");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ScansControllerTests.ReadJson(response);
        Assert.Equal(id, body.GetProperty("groupId").GetInt64());
        return body.GetProperty("genres").EnumerateArray().ToList();
    }

    private static async Task CreateGenre(HttpClient client, string name, params string[] aliases)
    {
        var response = await client.PostAsJsonAsync("/api/genres", new { name, aliases });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string? EntryName(JsonElement row) =>
        row.GetProperty("entry").ValueKind == JsonValueKind.Null ? null : row.GetProperty("entry").GetProperty("name").GetString();

    [Fact]
    public async Task Unknown_group_returns_404()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/genres")).StatusCode);
    }

    [Fact]
    public async Task Genres_resolve_through_keys_and_aliases_and_unresolved_ones_list_same_letter_suggestions()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        await CreateGenre(client, "Science Fiction", "SF");
        await SetGenres(client, id, "sf", "science-fiction", "Sciencefiction", "Horror");

        var genres = await Genres(client, id);

        Assert.Equal(["sf", "science-fiction", "Sciencefiction", "Horror"], genres.Select(g => g.GetProperty("name").GetString()));
        foreach (var resolved in genres.Take(2))
        {
            Assert.Equal("Science Fiction", EntryName(resolved));
            Assert.Equal(["SF"], resolved.GetProperty("entry").GetProperty("aliases").EnumerateArray().Select(a => a.GetString()));
            Assert.Empty(resolved.GetProperty("suggestions").EnumerateArray());
        }
        Assert.Null(EntryName(genres[2]));
        var suggestion = Assert.Single(genres[2].GetProperty("suggestions").EnumerateArray());
        Assert.Equal("Science Fiction", suggestion.GetProperty("entry").GetProperty("name").GetString());
        Assert.Equal("\"Sciencefiction\" and \"Science Fiction\" differ only in spacing or punctuation.",
            suggestion.GetProperty("reason").GetString());
        Assert.Null(EntryName(genres[3]));
        Assert.Empty(genres[3].GetProperty("suggestions").EnumerateArray());

        var catalog = await ScansControllerTests.ReadJson(await client.GetAsync("/api/genres"));
        Assert.Equal(1, catalog.GetArrayLength());
    }

    [Fact]
    public async Task Adding_a_genre_resolves_it_on_the_next_read()
    {
        var client = _factory.CreateClient();
        var id = await ScanAndTakeGroup(client);
        await SetGenres(client, id, "Horror");
        Assert.Null(EntryName(Assert.Single(await Genres(client, id))));

        await CreateGenre(client, "Horror");

        Assert.Equal("Horror", EntryName(Assert.Single(await Genres(client, id))));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
