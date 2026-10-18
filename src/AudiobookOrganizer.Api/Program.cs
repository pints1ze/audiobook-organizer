using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Data;
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

builder.Services.AddControllers();

var app = builder.Build();

app.Services.GetRequiredService<DatabaseInitializer>().Initialize();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
// Client routes get the SPA shell; unknown /api paths stay 404 (the more specific pattern wins).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
