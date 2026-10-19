using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Tests.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class MetadataControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();

    private HttpClient ClientWith(FakeHttpMessageHandler audible, FakeHttpMessageHandler openLibrary) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<AudibleClient>().ConfigurePrimaryHttpMessageHandler(() => audible);
                services.AddHttpClient<OpenLibraryClient>().ConfigurePrimaryHttpMessageHandler(() => openLibrary);
            }))
            .CreateClient();

    private static FakeHttpMessageHandler OpenLibraryFixture(string name) =>
        FakeHttpMessageHandler.Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "openlibrary", name)));

    private static FakeHttpMessageHandler Unavailable() => FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);

    [Theory]
    [InlineData("/api/metadata/search")]
    [InlineData("/api/metadata/search?keywords=")]
    [InlineData("/api/metadata/search?keywords=%20%20")]
    public async Task Blank_keywords_return_400_without_calling_a_provider(string url)
    {
        var audible = FakeHttpMessageHandler.Fixture("search-project-hail-mary.json");
        var openLibrary = OpenLibraryFixture("search-project-hail-mary.json");

        var response = await ClientWith(audible, openLibrary).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("keywords", out _));
        Assert.Empty(audible.Requests);
        Assert.Empty(openLibrary.Requests);
    }

    [Fact]
    public async Task Audible_results_win_and_open_library_is_never_called()
    {
        var audible = FakeHttpMessageHandler.Fixture("search-project-hail-mary.json");
        var openLibrary = OpenLibraryFixture("search-project-hail-mary.json");

        var response = await ClientWith(audible, openLibrary).GetAsync("/api/metadata/search?keywords=project%20hail%20mary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = body.RootElement.GetProperty("candidates")[0];
        Assert.Equal("audible", first.GetProperty("provider").GetString());
        Assert.Equal("B08G9PRS1K", first.GetProperty("asin").GetString());
        Assert.Equal(970, first.GetProperty("runtimeMinutes").GetInt32());
        Assert.Equal("Andy Weir", first.GetProperty("authors")[0].GetString());
        Assert.Equal(0, body.RootElement.GetProperty("providerErrors").GetArrayLength());
        Assert.Contains("keywords=project%20hail%20mary", Assert.Single(audible.Requests).RequestUri!.Query);
        Assert.Empty(openLibrary.Requests);
    }

    [Fact]
    public async Task Audible_failure_falls_back_to_open_library_and_reports_the_error()
    {
        var openLibrary = OpenLibraryFixture("search-project-hail-mary.json");

        var response = await ClientWith(Unavailable(), openLibrary).GetAsync("/api/metadata/search?keywords=project%20hail%20mary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var candidates = body.RootElement.GetProperty("candidates");
        Assert.True(candidates.GetArrayLength() > 0);
        Assert.All(candidates.EnumerateArray(), c => Assert.Equal("openlibrary", c.GetProperty("provider").GetString()));
        var error = Assert.Single(body.RootElement.GetProperty("providerErrors").EnumerateArray());
        Assert.StartsWith("Audible search failed", error.GetString());
    }

    [Fact]
    public async Task Empty_audible_result_falls_back_to_open_library_with_no_errors()
    {
        var audible = FakeHttpMessageHandler.Json("""{"products":[]}""");
        var openLibrary = OpenLibraryFixture("search-isbn-9780593135204.json");

        var response = await ClientWith(audible, openLibrary).GetAsync("/api/metadata/search?keywords=978-0-593-13520-4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var book = Assert.Single(body.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal("openlibrary", book.GetProperty("provider").GetString());
        Assert.Equal("9780593135204", book.GetProperty("isbn").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("providerErrors").GetArrayLength());
        Assert.Single(audible.Requests);
        Assert.Contains("isbn=9780593135204", Assert.Single(openLibrary.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task Both_providers_failing_returns_502_naming_both()
    {
        var response = await ClientWith(Unavailable(), Unavailable()).GetAsync("/api/metadata/search?keywords=anything");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(502, problem.GetProperty("status").GetInt32());
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("Audible", detail);
        Assert.Contains("Open Library", detail);
    }

    [Fact]
    public async Task Both_providers_finding_nothing_returns_200_with_empty_lists()
    {
        var audible = FakeHttpMessageHandler.Json("""{"products":[]}""");
        var openLibrary = FakeHttpMessageHandler.Json("""{"numFound":0,"docs":[]}""");

        var response = await ClientWith(audible, openLibrary).GetAsync("/api/metadata/search?keywords=zzzz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("providerErrors").GetArrayLength());
    }

    public void Dispose() => _factory.Dispose();
}
