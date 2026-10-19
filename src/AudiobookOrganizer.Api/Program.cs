using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Services.Grouping;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) => config
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

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
builder.Services.AddSingleton<PersonRepository>();
builder.Services.AddSingleton<PersonService>();

builder.Services.Configure<ToolSettings>(builder.Configuration.GetSection(ToolSettings.SectionName));
builder.Services.AddSingleton<FfmpegRunner>();
builder.Services.AddSingleton<ChapterReader>();
builder.Services.AddSingleton<AudioFileReader>();
builder.Services.AddSingleton<ScanRepository>();
builder.Services.AddSingleton<GroupRepository>();
builder.Services.AddSingleton<GroupingService>();
builder.Services.AddSingleton<AudiobookRepository>();
builder.Services.AddSingleton<AudiobookService>();
// Transient because it holds the typed CoverDownloadClient, as MetadataSearchService holds the providers.
builder.Services.AddTransient<CoverService>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<ScanRunner>();
builder.Services.AddSingleton<DashboardService>();

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

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
// Client routes get the SPA shell; unknown /api paths stay 404 (the more specific pattern wins).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
