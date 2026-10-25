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

    /// <summary>
    /// Off by default: the hosted <see cref="CandidateSearchWorker"/> searches the catalog after every scan, and most
    /// tests scan with the real providers registered. Tests that turn it on fake the providers and shorten
    /// CandidateSearch:DelayMs.
    /// </summary>
    public bool RunCandidateSearchWorker { get; init; }

    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-tests-{Guid.NewGuid():N}.db");

    /// <summary>Log files go here, never into the repo's data folder; deleted on dispose.</summary>
    public string LogDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-tests-logs-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", DatabasePath);
        builder.UseSetting("Logs:Path", Path.Combine(LogDirectory, "app-.log"));
        if (!RunProcessingWorker) builder.ConfigureTestServices(RemoveProcessingWorker);
        if (!RunCandidateSearchWorker) builder.ConfigureTestServices(RemoveCandidateSearchWorker);
    }

    /// <summary>Removes the worker's hosted-service registration; throws if it isn't registered.</summary>
    public static void RemoveProcessingWorker(IServiceCollection services) => services.Remove(services.Single(d =>
        d.ServiceType == typeof(IHostedService) && d.ImplementationFactory?.Method.ReturnType == typeof(ProcessingWorker)));

    /// <summary>Removes the background search's hosted-service registration; throws if it isn't registered.</summary>
    public static void RemoveCandidateSearchWorker(IServiceCollection services) => services.Remove(services.Single(d =>
        d.ServiceType == typeof(IHostedService) && d.ImplementationFactory?.Method.ReturnType == typeof(CandidateSearchWorker)));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
        // The host is gone, so the file sink has closed its file.
        if (Directory.Exists(LogDirectory)) Directory.Delete(LogDirectory, recursive: true);
    }
}
