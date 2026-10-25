using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Tests.Controllers;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Services;

public class CandidateSearchWorkerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new() { RunCandidateSearchWorker = true };
    private readonly ScanTestFolders _folders = new();
    private readonly FakeHttpMessageHandler _audible;
    private readonly FakeHttpMessageHandler _openLibrary = FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);

    /// <summary>Audible answers with the Project Hail Mary fixture while <see cref="_audibleUp"/>, else 503.</summary>
    private bool _audibleUp = true;

    public CandidateSearchWorkerTests()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "audible", "search-project-hail-mary.json"));
        _audible = new FakeHttpMessageHandler(_ => _audibleUp
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture, System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
    }

    private WebApplicationFactory<Program> Host(int delayMs, bool worker = true) => _factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("CandidateSearch:DelayMs", delayMs.ToString(CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<AudibleClient>().ConfigurePrimaryHttpMessageHandler(() => _audible);
            services.AddHttpClient<OpenLibraryClient>().ConfigurePrimaryHttpMessageHandler(() => _openLibrary);
            if (!worker) AudiobookOrganizerWebApplicationFactory.RemoveCandidateSearchWorker(services);
        });
    });

    /// <summary>
    /// Adds a book beside the default folder's one: a copy of the M4B fixture in its own folder. Returns the number of
    /// books a scan now finds.
    /// </summary>
    private int AddBook(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_folders.Incoming, name)).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), Path.Combine(folder, "book.m4b"));
        return 1 + Directory.GetDirectories(_folders.Incoming).Length;
    }

    private async Task<List<long>> ScanAndListGroups(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return list.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64()).ToList();
    }

    private long SearchCount()
    {
        using var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open();
        return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM CandidateSearch");
    }

    /// <summary>Waits until <paramref name="count"/> books have a stored search, failing after 10 seconds.</summary>
    private async Task WaitForSearches(long count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (SearchCount() < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Only {SearchCount()} of {count} books were searched within 10 seconds.");
            await Task.Delay(50);
        }
    }

    private static async Task<JsonElement> Candidates(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}/candidates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    [Fact]
    public async Task After_a_scan_every_new_book_gets_a_stored_search_without_a_post()
    {
        var books = AddBook("Second Book");
        var client = Host(0).CreateClient();
        var ids = await ScanAndListGroups(client);
        Assert.Equal(books, ids.Count);

        await WaitForSearches(ids.Count);

        foreach (var id in ids)
        {
            var stored = await Candidates(client, id);
            Assert.Equal(0, stored.GetProperty("errors").GetArrayLength());
            var candidates = stored.GetProperty("candidates").EnumerateArray().ToList();
            Assert.InRange(candidates.Count, 1, CandidateService.MaxStored);
            Assert.All(candidates, c => Assert.Equal("audible", c.GetProperty("candidate").GetProperty("provider").GetString()));
        }
        Assert.Equal(ids.Count, _audible.Requests.Count);
    }

    [Fact]
    public async Task A_book_with_a_chosen_match_or_a_stored_search_is_not_searched_again()
    {
        AddBook("Second Book");
        var books = AddBook("Third Book");
        // Set up without the worker: one book gets a match, one a stored search.
        var setup = Host(0, worker: false).CreateClient();
        var ids = await ScanAndListGroups(setup);
        Assert.Equal(3, books);
        Assert.Equal(books, ids.Count);
        var put = await setup.PutAsJsonAsync($"/api/groups/{ids[0]}/match", new
        {
            provider = "audible", asin = "B0TEST0001", title = "The Test Book", subtitle = (string?)null,
            authors = new[] { "Ann Author" }, narrators = Array.Empty<string>(), series = (object?)null,
            publisher = (string?)null, releaseDate = (string?)null, language = "english", runtimeMinutes = 600,
            format = "unabridged", coverUrl = (string?)null, isbn = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await setup.PostAsync($"/api/groups/{ids[1]}/candidates", null)).StatusCode);
        Assert.Single(_audible.Requests);

        // A host with the worker: its startup pass searches only the third book.
        var client = Host(0).CreateClient();
        await WaitForSearches(2);

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/groups/{ids[0]}/candidates")).StatusCode);
        Assert.Equal(0, (await Candidates(client, ids[2])).GetProperty("errors").GetArrayLength());
        Assert.Equal(2, _audible.Requests.Count);
    }

    [Fact]
    public async Task With_both_providers_down_each_book_stores_the_error_and_the_worker_goes_on()
    {
        _audibleUp = false;
        var books = AddBook("Second Book");
        var client = Host(0).CreateClient();
        var ids = await ScanAndListGroups(client);
        Assert.Equal(books, ids.Count);

        await WaitForSearches(ids.Count);

        foreach (var id in ids)
        {
            var stored = await Candidates(client, id);
            Assert.Equal(0, stored.GetProperty("candidates").GetArrayLength());
            Assert.NotEqual(0, stored.GetProperty("errors").GetArrayLength());
        }
    }

    [Fact]
    public async Task The_worker_waits_the_configured_delay_between_two_searches()
    {
        var books = AddBook("Second Book");
        var client = Host(300).CreateClient();
        var ids = await ScanAndListGroups(client);
        Assert.Equal(books, ids.Count);

        await WaitForSearches(ids.Count);

        var times = new List<DateTimeOffset>();
        foreach (var id in ids) times.Add((await Candidates(client, id)).GetProperty("searchedAtUtc").GetDateTimeOffset());
        times.Sort();
        for (var i = 1; i < times.Count; i++) Assert.True(times[i] - times[i - 1] >= TimeSpan.FromMilliseconds(300));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
