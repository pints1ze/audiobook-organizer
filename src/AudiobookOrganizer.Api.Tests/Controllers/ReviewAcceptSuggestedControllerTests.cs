using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Tests.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class ReviewAcceptSuggestedControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    /// <summary>Stores a search for the group with these audible candidates, rank by order.</summary>
    internal static void SeedCandidates(
        AudiobookOrganizerWebApplicationFactory factory, long groupId, params (string Title, int Score)[] candidates) =>
        factory.Services.GetRequiredService<CandidateRepository>().Replace(groupId, DateTimeOffset.UtcNow.ToString("O"), "[]",
            candidates.Select((c, i) => new MetadataCandidate
            {
                Provider = "audible",
                CandidateJson = JsonSerializer.Serialize(new MetadataCandidateDto("audible", $"B0SUGGEST{i}", c.Title, null,
                    ["Ann Author"], ["Ned Narrator"], null, null, null, null, null, null, null, null), JsonSerializerOptions.Web),
                Score = c.Score,
            }).ToList());

    /// <summary>Replaces incoming's root files with <paramref name="count"/> two-file books, scans, returns their ids.</summary>
    private async Task<List<long>> ScanBooks(HttpClient client, int count)
    {
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        for (var i = 0; i < count; i++)
        {
            var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, $"book{i}")).FullName;
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track1.mp3"));
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        }
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());

        var ids = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups")))
            .GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(count, ids.Count);
        return ids;
    }

    private static async Task<HttpResponseMessage> Accept(HttpClient client, object body) =>
        await client.PostAsJsonAsync("/api/review/accept-suggested", body);

    private static async Task<JsonElement?> Match(HttpClient client, long groupId)
    {
        var response = await client.GetAsync($"/api/groups/{groupId}/match");
        return response.StatusCode == HttpStatusCode.OK ? await ScansControllerTests.ReadJson(response) : null;
    }

    private static List<(long GroupId, bool Accepted, string? Error)> Results(JsonElement body) =>
        body.GetProperty("results").EnumerateArray().Select(r => (r.GetProperty("groupId").GetInt64(),
            r.GetProperty("accepted").GetBoolean(), r.GetProperty("error").GetString())).ToList();

    [Fact]
    public async Task Two_searched_books_are_both_accepted_with_their_best_candidate()
    {
        var client = _factory.CreateClient();
        var ids = await ScanBooks(client, 2);
        SeedCandidates(_factory, ids[0], ("First Best", 90), ("First Other", 30));
        SeedCandidates(_factory, ids[1], ("Second Best", 70));

        var response = await Accept(client, new { groupIds = new[] { ids[1], ids[0] } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(ids[1], true, null), (ids[0], true, null)], Results(await ScansControllerTests.ReadJson(response)));
        Assert.Equal("First Best", (await Match(client, ids[0]))!.Value.GetProperty("match").GetProperty("title").GetString());
        Assert.Equal("Second Best", (await Match(client, ids[1]))!.Value.GetProperty("match").GetProperty("title").GetString());
        var review = await ScansControllerTests.ReadJson(await client.GetAsync("/api/review"));
        Assert.All(review.GetProperty("books").EnumerateArray(),
            b => Assert.NotEqual("NeedsMatch", b.GetProperty("stage").GetString()));
    }

    [Fact]
    public async Task A_mixed_request_accepts_one_and_skips_the_rest_in_order_with_their_errors()
    {
        var client = _factory.CreateClient();
        var ids = await ScanBooks(client, 3);
        var (searched, matched, unsearched) = (ids[0], ids[1], ids[2]);
        SeedCandidates(_factory, searched, ("Suggested", 60));
        SeedCandidates(_factory, matched, ("Not This", 99));
        var pick = await client.PutAsJsonAsync($"/api/groups/{matched}/match", new
        {
            provider = "audible", asin = "B0PICKED01", title = "Picked By Hand", authors = new[] { "Ann Author" },
            narrators = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, pick.StatusCode);
        var before = (await Match(client, matched))!.Value.GetRawText();

        var response = await Accept(client, new { groupIds = new[] { searched, matched, unsearched, 999999L, searched } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                (searched, true, null),
                (matched, false, "This book already has a match."),
                (unsearched, false, "No suggested match."),
                (999999L, false, "This book no longer exists."),
            ],
            Results(await ScansControllerTests.ReadJson(response)));
        Assert.Equal("Suggested", (await Match(client, searched))!.Value.GetProperty("match").GetProperty("title").GetString());
        Assert.Equal(before, (await Match(client, matched))!.Value.GetRawText());
        Assert.Null(await Match(client, unsearched));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("tooMany")]
    public async Task Missing_empty_or_too_many_group_ids_are_400_with_a_groupIds_error(string kind)
    {
        var client = _factory.CreateClient();
        object body = kind switch
        {
            "missing" => new { },
            "empty" => new { groupIds = Array.Empty<long>() },
            _ => new { groupIds = Enumerable.Range(1, 501).Select(i => (long)i).ToArray() },
        };

        var response = await Accept(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ScansControllerTests.ReadJson(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("groupIds", out _));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
