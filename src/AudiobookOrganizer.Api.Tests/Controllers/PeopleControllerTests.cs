using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class PeopleControllerTests : IDisposable
{
    private record PersonBody(int Id, string Name, List<string> Aliases);

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();

    private async Task<PersonBody> Create(HttpClient client, string name, params string[] aliases)
    {
        var response = await client.PostAsJsonAsync("/api/people", new { name, aliases });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonBody>())!;
    }

    [Fact]
    public async Task Create_returns_201_then_list_and_resolve_return_the_person()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/people",
            new { name = "  Stephen   King ", aliases = new[] { "Richard Bachman", "John Swithen" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<PersonBody>())!;
        Assert.Equal("Stephen King", created.Name);
        Assert.Equal(["John Swithen", "Richard Bachman"], created.Aliases);

        var list = await client.GetFromJsonAsync<List<PersonBody>>("/api/people");
        Assert.Equal(created, Assert.Single(list!), PersonComparer);

        var byLastFirst = await client.GetFromJsonAsync<PersonBody>("/api/people/resolve?name=KING,%20STEPHEN");
        Assert.Equal(created, byLastFirst, PersonComparer);
        var byAlias = await client.GetFromJsonAsync<PersonBody>("/api/people/resolve?name=richard%20bachman");
        Assert.Equal(created, byAlias, PersonComparer);
    }

    [Fact]
    public async Task List_is_ordered_by_name_ignoring_case()
    {
        var client = _factory.CreateClient();
        await Create(client, "stephen King");
        await Create(client, "Neil Gaiman");
        await Create(client, "andy Weir");

        var list = await client.GetFromJsonAsync<List<PersonBody>>("/api/people");

        Assert.Equal(["andy Weir", "Neil Gaiman", "stephen King"], list!.Select(p => p.Name));
    }

    [Fact]
    public async Task Aliases_that_repeat_the_name_or_each_other_are_dropped()
    {
        var client = _factory.CreateClient();

        var created = await Create(client, "Stephen King", "King, Stephen", "STEPHEN KING", "Richard Bachman",
            "richard  bachman");

        Assert.Equal(["Richard Bachman"], created.Aliases);
    }

    [Fact]
    public async Task Second_person_with_a_clashing_name_or_alias_returns_400()
    {
        var client = _factory.CreateClient();
        await Create(client, "Stephen King", "Richard Bachman");

        var nameClash = await client.PostAsJsonAsync("/api/people", new { name = "Richard Bachman", aliases = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, nameClash.StatusCode);
        Assert.Equal("Already used by Stephen King.", (await ReadErrors(nameClash)).GetProperty("name")[0].GetString());

        var aliasClash = await client.PostAsJsonAsync("/api/people", new { name = "Someone Else", aliases = new[] { "king, stephen" } });
        Assert.Equal(HttpStatusCode.BadRequest, aliasClash.StatusCode);
        Assert.Contains("Already used by Stephen King.",
            (await ReadErrors(aliasClash)).GetProperty("aliases")[0].GetString());

        Assert.Single((await client.GetFromJsonAsync<List<PersonBody>>("/api/people"))!);
    }

    [Fact]
    public async Task Blank_name_or_alias_returns_400()
    {
        var client = _factory.CreateClient();

        var blankName = await client.PostAsJsonAsync("/api/people", new { name = "   ", aliases = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        Assert.True((await ReadErrors(blankName)).TryGetProperty("name", out _));

        var blankAlias = await client.PostAsJsonAsync("/api/people", new { name = "Stephen King", aliases = new[] { " " } });
        Assert.Equal(HttpStatusCode.BadRequest, blankAlias.StatusCode);
        Assert.True((await ReadErrors(blankAlias)).TryGetProperty("aliases", out _));
    }

    [Fact]
    public async Task Put_replaces_the_name_and_aliases()
    {
        var client = _factory.CreateClient();
        var created = await Create(client, "Steven King", "Richard Bachman");

        var response = await client.PutAsJsonAsync($"/api/people/{created.Id}",
            new { name = "Stephen King", aliases = new[] { "John Swithen" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<PersonBody>())!;
        Assert.Equal(new PersonBody(created.Id, "Stephen King", ["John Swithen"]), updated, PersonComparer);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/people/resolve?name=Richard%20Bachman")).StatusCode);
        Assert.Equal(created.Id,
            (await client.GetFromJsonAsync<PersonBody>("/api/people/resolve?name=john%20swithen"))!.Id);
    }

    [Fact]
    public async Task Put_may_keep_the_persons_own_name_and_aliases()
    {
        var client = _factory.CreateClient();
        var created = await Create(client, "Stephen King", "Richard Bachman");

        var response = await client.PutAsJsonAsync($"/api/people/{created.Id}",
            new { name = "KING, Stephen", aliases = new[] { "Richard Bachman" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new PersonBody(created.Id, "KING, Stephen", ["Richard Bachman"]),
            await response.Content.ReadFromJsonAsync<PersonBody>(), PersonComparer);
    }

    [Fact]
    public async Task Put_to_an_unknown_id_returns_404()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/people/999", new { name = "Stephen King" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_unknown_name_returns_404_and_blank_name_returns_400()
    {
        var client = _factory.CreateClient();
        await Create(client, "Stephen King");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/people/resolve?name=Neil%20Gaiman")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/people/resolve?name=%20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/people/resolve")).StatusCode);
    }

    private static readonly IEqualityComparer<PersonBody?> PersonComparer = EqualityComparer<PersonBody?>.Create(
        (a, b) => a is not null && b is not null && a.Id == b.Id && a.Name == b.Name && a.Aliases.SequenceEqual(b.Aliases),
        p => p?.Id ?? 0);

    private static async Task<JsonElement> ReadErrors(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").Clone();
    }

    public void Dispose() => _factory.Dispose();
}
