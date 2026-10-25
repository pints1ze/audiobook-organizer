using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Services.Grouping;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) =>
{
    config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services);

    // Daily files beside the database (the data folder); an empty path turns the file sink off.
    var logPath = builder.Configuration["Logs:Path"];
    if (string.IsNullOrWhiteSpace(logPath)) return;
    var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
    config.WriteTo.File(Path.GetFullPath(logPath, contentRoot),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
},
// The host owns and disposes its own logger. Otherwise disposal goes through Log.CloseAndFlush(), which closes
// whichever host set the static logger last, leaving this host's log file open (seen with parallel test hosts).
preserveStaticLogger: true);

builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.AddSingleton(services =>
{
    var settings = services.GetRequiredService<IOptions<DatabaseSettings>>().Value;
    var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
    return new SqliteConnectionFactory(Path.GetFullPath(settings.Path, contentRoot));
});
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CanonicalNameRepository>();
builder.Services.AddSingleton<CanonicalNameService>();

builder.Services.Configure<ToolSettings>(builder.Configuration.GetSection(ToolSettings.SectionName));
builder.Services.AddSingleton<FfmpegRunner>();
builder.Services.AddSingleton<ChapterReader>();
builder.Services.AddSingleton<AudioFileReader>();
builder.Services.AddSingleton<ScanRepository>();
builder.Services.AddSingleton<GroupRepository>();
builder.Services.AddSingleton<GroupingService>();
builder.Services.AddSingleton<AudiobookRepository>();
builder.Services.AddSingleton<AudiobookService>();
builder.Services.AddSingleton<NamingPreviewService>();
builder.Services.AddSingleton<ProcessingRepository>();
builder.Services.AddSingleton<TagWriter>();
builder.Services.AddSingleton<ProcessingService>();
// One instance: JobsController wakes the same worker the host runs.
builder.Services.AddSingleton<ProcessingWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<ProcessingWorker>());
// Transient because it holds the typed CoverDownloadClient, as MetadataSearchService holds the providers.
builder.Services.AddTransient<CoverService>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<ScanRunner>();
builder.Services.AddSingleton<DashboardService>();
// Transient because it holds the transient CoverService.
builder.Services.AddTransient<ReviewService>();

builder.Services.AddHttpClient<AudibleClient>(http =>
{
    http.BaseAddress = new Uri("https://api.audible.com/");
    http.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<OpenLibraryClient>(http =>
{
    http.BaseAddress = new Uri("https://openlibrary.org/");
    http.Timeout = TimeSpan.FromSeconds(15);
    // Open Library asks clients to identify themselves.
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookOrganizer/1.0 (self-hosted)");
});
// The catalogs' image CDNs are their own system; CoverService limits which hosts it's asked to fetch from.
builder.Services.AddHttpClient<CoverDownloadClient>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(15);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("AudiobookOrganizer/1.0 (self-hosted)");
});
// Registration order is the fallback order: Audible first, then Open Library.
builder.Services.AddTransient<IMetadataProvider>(services => services.GetRequiredService<AudibleClient>());
builder.Services.AddTransient<IMetadataProvider>(services => services.GetRequiredService<OpenLibraryClient>());
builder.Services.AddTransient<MetadataSearchService>();

builder.Services.AddControllers();

var app = builder.Build();

app.Services.GetRequiredService<DatabaseInitializer>().Initialize();
// Nothing can still be scanning at startup, so a Running row is left over from a stopped or crashed process.
app.Services.GetRequiredService<ScanRepository>()
    .FailInterruptedRuns("Interrupted by an application restart.", DateTime.UtcNow.ToString("O"));
// Likewise for processing: a job cut off while deleting sources finishes its deletions and succeeds; any other is
// failed and its copies removed. The worker only starts with the host, after this, and then runs the Queued jobs.
app.Services.GetRequiredService<ProcessingService>().FailInterruptedJobs();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
// Client routes get the SPA shell; unknown /api paths stay 404 (the more specific pattern wins).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
