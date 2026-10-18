using System.Net;
using System.Net.Http.Json;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class HealthControllerTests(AudiobookOrganizerWebApplicationFactory factory)
    : IClassFixture<AudiobookOrganizerWebApplicationFactory>
{
    private record HealthBody(string Status, string Database);

    [Fact]
    public async Task Get_returns_ok_with_database_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("ok", body.Database);
    }

    [Fact]
    public void Startup_creates_database_file_at_configured_path()
    {
        _ = factory.CreateClient();

        Assert.True(File.Exists(factory.DatabasePath), $"Expected database file at {factory.DatabasePath}");
    }
}
