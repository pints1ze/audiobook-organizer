using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class StaticFilesTests : IClassFixture<AudiobookOrganizerWebApplicationFactory>, IDisposable
{
    private const string IndexHtml = "<!doctype html><html><body>spa shell</body></html>";

    private readonly string _webRoot =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-wwwroot-{Guid.NewGuid():N}");

    private readonly WebApplicationFactory<Program> _factory;

    public StaticFilesTests(AudiobookOrganizerWebApplicationFactory factory)
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
        _factory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
    }

    [Fact]
    public async Task Client_route_returns_index_html()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/some/client/route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_api_path_returns_404_not_the_spa_fallback()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("spa shell", await response.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
