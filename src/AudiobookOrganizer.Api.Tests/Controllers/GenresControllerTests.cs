using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GenresControllerTests : IDisposable
{
    private record EntryBody(int Id, string Name, List<string> Aliases);

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();

    private static async Task<EntryBody> Create(HttpClient client, string url, string name, params string[] aliases)
    {
        var response = await client.PostAsJsonAsync(url, new { name, aliases });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EntryBody>())!;
    }

    [Fact]
    public async Task Create_returns_201_then_list_and_resolve_return_the_genre()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/genres",
            new { name = " Science  Fiction ", aliases = new[] { "Sci-Fi" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EntryBody>())!;
        Assert.Equal("Science Fiction", created.Name);
        Assert.Equal(["Sci-Fi"], created.Aliases);

        var list = await client.GetFromJsonAsync<List<EntryBody>>("/api/genres");
        Assert.Equal(created, Assert.Single(list!), EntryComparer);

        var byVariant = await client.GetFromJsonAsync<EntryBody>("/api/genres/resolve?name=science-fiction");
        Assert.Equal(created, byVariant, EntryComparer);
        var byAlias = await client.GetFromJsonAsync<EntryBody>("/api/genres/resolve?name=sci%20fi");
        Assert.Equal(created, byAlias, EntryComparer);
    }

    [Fact]
    public async Task List_is_ordered_by_name_ignoring_case_and_separate_from_people()
    {
        var client = _factory.CreateClient();
        await Create(client, "/api/genres", "thriller");
        await Create(client, "/api/genres", "Horror");
        await Create(client, "/api/genres", "biography");
        await Create(client, "/api/people", "Stephen King");

        var list = await client.GetFromJsonAsync<List<EntryBody>>("/api/genres");

        Assert.Equal(["biography", "Horror", "thriller"], list!.Select(g => g.Name));
    }

    [Fact]
    public async Task Aliases_that_repeat_the_name_or_each_other_are_dropped()
    {
        var client = _factory.CreateClient();

        var created = await Create(client, "/api/genres", "Science Fiction", "science-fiction", "Sci-Fi", "sci fi");

        Assert.Equal(["Sci-Fi"], created.Aliases);
    }

    [Fact]
    public async Task Second_genre_with_a_clashing_name_or_alias_returns_400()
    {
        var client = _factory.CreateClient();
        await Create(client, "/api/genres", "Science Fiction", "Sci-Fi");

        var nameClash = await client.PostAsJsonAsync("/api/genres", new { name = "SCI FI", aliases = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, nameClash.StatusCode);
        Assert.Equal("Already used by Science Fiction.", (await ReadErrors(nameClash)).GetProperty("name")[0].GetString());

        var aliasClash = await client.PostAsJsonAsync("/api/genres", new { name = "Fantasy", aliases = new[] { "science_fiction" } });
        Assert.Equal(HttpStatusCode.BadRequest, aliasClash.StatusCode);
        Assert.Contains("Already used by Science Fiction.",
            (await ReadErrors(aliasClash)).GetProperty("aliases")[0].GetString());

        Assert.Single((await client.GetFromJsonAsync<List<EntryBody>>("/api/genres"))!);
    }

    [Fact]
    public async Task Blank_name_or_alias_returns_400()
    {
        var client = _factory.CreateClient();

        var blankName = await client.PostAsJsonAsync("/api/genres", new { name = "   ", aliases = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        Assert.True((await ReadErrors(blankName)).TryGetProperty("name", out _));

        var blankAlias = await client.PostAsJsonAsync("/api/genres", new { name = "Horror", aliases = new[] { " " } });
        Assert.Equal(HttpStatusCode.BadRequest, blankAlias.StatusCode);
        Assert.True((await ReadErrors(blankAlias)).TryGetProperty("aliases", out _));
    }

    [Fact]
    public async Task Put_replaces_the_name_and_aliases()
    {
        var client = _factory.CreateClient();
        var created = await Create(client, "/api/genres", "Science Fiction", "Sci-Fi");

        var response = await client.PutAsJsonAsync($"/api/genres/{created.Id}",
            new { name = "Science Fiction & Fantasy", aliases = new[] { "SFF" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<EntryBody>())!;
        Assert.Equal(new EntryBody(created.Id, "Science Fiction & Fantasy", ["SFF"]), updated, EntryComparer);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/genres/resolve?name=Sci-Fi")).StatusCode);
        Assert.Equal(created.Id, (await client.GetFromJsonAsync<EntryBody>("/api/genres/resolve?name=sff"))!.Id);
    }

    [Fact]
    public async Task Put_to_an_unknown_id_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/genres/999", new { name = "Horror" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_unknown_genre_returns_404_and_blank_returns_400()
    {
        var client = _factory.CreateClient();
        await Create(client, "/api/genres", "Horror");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/genres/resolve?name=Romance")).StatusCode);
        var blank = await client.GetAsync("/api/genres/resolve?name=%20");
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Enter a genre to resolve.", (await ReadErrors(blank)).GetProperty("name")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/genres/resolve")).StatusCode);
    }

    [Fact]
    public async Task A_person_and_a_genre_may_share_a_name()
    {
        var client = _factory.CreateClient();
        var person = await Create(client, "/api/people", "Horror", "Science Fiction");
        var genre = await Create(client, "/api/genres", "Horror", "Science Fiction");

        Assert.Equal(person.Id,
            (await client.GetFromJsonAsync<EntryBody>("/api/people/resolve?name=science%20fiction"))!.Id);
        Assert.Equal(genre.Id,
            (await client.GetFromJsonAsync<EntryBody>("/api/genres/resolve?name=science%20fiction"))!.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/people/resolve?name=science-fiction")).StatusCode);
    }

    private static readonly IEqualityComparer<EntryBody?> EntryComparer = EqualityComparer<EntryBody?>.Create(
        (a, b) => a is not null && b is not null && a.Id == b.Id && a.Name == b.Name && a.Aliases.SequenceEqual(b.Aliases),
        e => e?.Id ?? 0);

    private static async Task<JsonElement> ReadErrors(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").Clone();
    }

    public void Dispose() => _factory.Dispose();
}
