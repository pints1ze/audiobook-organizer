using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace AudiobookOrganizer.Api.Tests;

public class LoggingTests
{
    [Fact]
    public async Task Startup_writes_a_daily_log_file_under_the_configured_path()
    {
        using var factory = new AudiobookOrganizerWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = Assert.Single(Directory.GetFiles(factory.LogDirectory, "app-*.log"));
        Assert.Matches(@"app-\d{8}\.log$", file);
        // The file sink holds the file open while the host runs, so share it.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        Assert.False(string.IsNullOrWhiteSpace(await reader.ReadToEndAsync()));
    }

    [Fact]
    public async Task Empty_log_path_writes_no_log_files()
    {
        using var factory = new AudiobookOrganizerWebApplicationFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseSetting("Logs:Path", "")).CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Directory.Exists(factory.LogDirectory));
    }

    [Fact]
    public void Dispose_deletes_the_log_folder()
    {
        var factory = new AudiobookOrganizerWebApplicationFactory();
        _ = factory.CreateClient();

        factory.Dispose();

        Assert.False(Directory.Exists(factory.LogDirectory));
    }
}
