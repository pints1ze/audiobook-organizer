using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AudiobookOrganizer.Api.Tests;

public class AudiobookOrganizerWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Off by default: the hosted <see cref="ProcessingWorker"/> reads the queue on its own thread as the host starts,
    /// which races tests that lock or replace the database file. Tests of processing turn it on.
    /// </summary>
    public bool RunProcessingWorker { get; init; }

    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", DatabasePath);
        if (!RunProcessingWorker) builder.ConfigureTestServices(RemoveProcessingWorker);
    }

    /// <summary>Removes the worker's hosted-service registration; throws if it isn't registered.</summary>
    public static void RemoveProcessingWorker(IServiceCollection services) => services.Remove(services.Single(d =>
        d.ServiceType == typeof(IHostedService) && d.ImplementationFactory?.Method.ReturnType == typeof(ProcessingWorker)));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
    }
}
