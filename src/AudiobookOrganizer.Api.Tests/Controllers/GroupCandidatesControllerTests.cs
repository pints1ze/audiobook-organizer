using System.Net;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupCandidatesControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    /// <summary>Audible answers with the Project Hail Mary fixture while <see cref="_audibleUp"/>, else 503.</summary>
    private bool _audibleUp = true;

    private HttpClient Client()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "audible", "search-project-hail-mary.json"));
        var audible = new FakeHttpMessageHandler(_ => _audibleUp
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture, System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        var openLibrary = FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
        return _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<AudibleClient>().ConfigurePrimaryHttpMessageHandler(() => audible);
                services.AddHttpClient<OpenLibraryClient>().ConfigurePrimaryHttpMessageHandler(() => openLibrary);
            }))
            .CreateClient();
    }

    private async Task Scan(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
    }

    private async Task<long> ScanAndTakeGroup(HttpClient client)
    {
        await Scan(client);
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return list.GetProperty("groups")[0].GetProperty("id").GetInt64();
    }

    private long Count(string table, long groupId)
    {
        using var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open();
        return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table} WHERE GroupId = @groupId", new { groupId });
    }

    [Fact]
    public async Task A_book_is_unsearched_until_post_stores_its_top_five_candidates_best_first()
    {
        var client = Client();
        var id = await ScanAndTakeGroup(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/groups/{id}/candidates")).StatusCode);

        var post = await client.PostAsync($"/api/groups/{id}/candidates", null);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var stored = await ScansControllerTests.ReadJson(post);
        Assert.Equal(id, stored.GetProperty("groupId").GetInt64());
        Assert.True(stored.GetProperty("searchedAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(0, stored.GetProperty("errors").GetArrayLength());
        var candidates = stored.GetProperty("candidates").EnumerateArray().ToList();
        Assert.InRange(candidates.Count, 1, 5);
        var scores = candidates.Select(c => c.GetProperty("score").GetInt32()).ToList();
        Assert.All(scores, s => Assert.InRange(s, 0, 100));
        Assert.Equal(scores.OrderByDescending(s => s), scores);
        Assert.All(candidates, c => Assert.Equal("audible", c.GetProperty("candidate").GetProperty("provider").GetString()));
        Assert.Equal(candidates.Count, Count("MetadataCandidate", id));

        var get = await client.GetAsync($"/api/groups/{id}/candidates");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(stored.GetRawText(), (await ScansControllerTests.ReadJson(get)).GetRawText());
    }

    [Fact]
    public async Task Unknown_groups_are_404_for_get_and_post()
    {
        var client = Client();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/groups/999999/candidates", null)).StatusCode);
    }

    [Fact]
    public async Task A_failed_search_is_stored_as_an_error_and_a_later_success_replaces_it()
    {
        var client = Client();
        var id = await ScanAndTakeGroup(client);
        _audibleUp = false;

        var failed = await client.PostAsync($"/api/groups/{id}/candidates", null);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        var failedBody = await ScansControllerTests.ReadJson(failed);
        Assert.Equal(0, failedBody.GetProperty("candidates").GetArrayLength());
        var error = Assert.Single(failedBody.GetProperty("errors").EnumerateArray()).GetString();
        Assert.Contains("Audible search failed", error);
        var get = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}/candidates"));
        Assert.Equal(failedBody.GetRawText(), get.GetRawText());

        _audibleUp = true;
        var ok = await ScansControllerTests.ReadJson(await client.PostAsync($"/api/groups/{id}/candidates", null));
        Assert.True(ok.GetProperty("candidates").GetArrayLength() > 0);
        Assert.Equal(0, ok.GetProperty("errors").GetArrayLength());
        Assert.Equal(1, Count("CandidateSearch", id));
    }

    [Fact]
    public async Task A_rescan_that_replaces_an_uncorrected_group_removes_its_candidates()
    {
        var client = Client();
        var id = await ScanAndTakeGroup(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/groups/{id}/candidates", null)).StatusCode);
        Assert.Equal(1, Count("CandidateSearch", id));

        await Scan(client);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{id}/candidates")).StatusCode);
        Assert.Equal(0, Count("CandidateSearch", id));
        Assert.Equal(0, Count("MetadataCandidate", id));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
