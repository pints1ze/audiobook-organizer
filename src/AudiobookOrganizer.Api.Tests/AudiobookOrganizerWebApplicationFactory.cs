using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Tests;

public class AudiobookOrganizerWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", DatabasePath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
    }
}
