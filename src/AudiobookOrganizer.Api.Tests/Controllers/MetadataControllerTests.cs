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

    private HttpClient ClientWith(FakeHttpMessageHandler handler) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddHttpClient<AudibleClient>().ConfigurePrimaryHttpMessageHandler(() => handler)))
            .CreateClient();

    [Theory]
    [InlineData("/api/metadata/search")]
    [InlineData("/api/metadata/search?keywords=")]
    [InlineData("/api/metadata/search?keywords=%20%20")]
    public async Task Blank_keywords_return_400_without_calling_the_provider(string url)
    {
        var handler = FakeHttpMessageHandler.Fixture("search-project-hail-mary.json");

        var response = await ClientWith(handler).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("keywords", out _));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Search_returns_candidates_from_the_provider()
    {
        var handler = FakeHttpMessageHandler.Fixture("search-project-hail-mary.json");

        var response = await ClientWith(handler).GetAsync("/api/metadata/search?keywords=project%20hail%20mary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = body.RootElement[0];
        Assert.Equal("audible", first.GetProperty("provider").GetString());
        Assert.Equal("B08G9PRS1K", first.GetProperty("asin").GetString());
        Assert.Equal(970, first.GetProperty("runtimeMinutes").GetInt32());
        Assert.Equal("Andy Weir", first.GetProperty("authors")[0].GetString());
        Assert.Contains("keywords=project%20hail%20mary", Assert.Single(handler.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task Provider_failure_returns_502_problem_naming_the_provider()
    {
        var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);

        var response = await ClientWith(handler).GetAsync("/api/metadata/search?keywords=anything");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(502, problem.GetProperty("status").GetInt32());
        Assert.StartsWith("Audible search failed", problem.GetProperty("detail").GetString());
    }

    public void Dispose() => _factory.Dispose();
}
