using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class SettingsControllerTests : IDisposable
{
    private record SettingsBody(
        string? IncomingPath, string? LibraryPath, string? DirectoryTemplate, string? FileTemplate, bool KeepSourceFiles);

    private const string DirTemplate = "%artist%/[%series%/Book %series-part:2% - ]%album%";
    private const string FileTemplate = "[%track:3%. ]%album% (%artist%)";

    private static readonly SettingsBody Fresh = new(null, null, DirTemplate, FileTemplate, false);

    private static SettingsBody Body(string? incoming, string? library, bool keep) =>
        new(incoming, library, DirTemplate, FileTemplate, keep);

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-settings-api-{Guid.NewGuid():N}");

    private readonly string _incoming;
    private readonly string _library;

    public SettingsControllerTests()
    {
        _incoming = Directory.CreateDirectory(Path.Combine(_root, "incoming")).FullName;
        _library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
    }

    [Fact]
    public async Task Get_on_fresh_database_returns_null_paths_and_default_templates()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Fresh, await response.Content.ReadFromJsonAsync<SettingsBody>());
    }

    [Fact]
    public async Task Valid_put_returns_saved_values_and_get_round_trips_them()
    {
        var client = _factory.CreateClient();
        var expected = Body(_incoming, _library, true);

        var put = await client.PutAsJsonAsync("/api/settings", expected);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(expected, await put.Content.ReadFromJsonAsync<SettingsBody>());
        Assert.Equal(expected, await client.GetFromJsonAsync<SettingsBody>("/api/settings"));
    }

    [Fact]
    public async Task Settings_survive_a_restart()
    {
        var expected = Body(_incoming, _library, true);
        await _factory.CreateClient().PutAsJsonAsync("/api/settings", expected);

        // A second host over the same database file: new DI container, same SQLite file.
        using var restarted = _factory.WithWebHostBuilder(_ => { });
        var body = await restarted.CreateClient().GetFromJsonAsync<SettingsBody>("/api/settings");

        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task Invalid_library_put_returns_400_and_keeps_previous_values()
    {
        var client = _factory.CreateClient();
        var previous = Body(_incoming, _library, false);
        await client.PutAsJsonAsync("/api/settings", previous);

        var response = await client.PutAsJsonAsync("/api/settings",
            Body(_incoming, Path.Combine(_root, "missing"), true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrors(response);
        Assert.True(errors.TryGetProperty("libraryPath", out var messages));
        Assert.False(string.IsNullOrWhiteSpace(messages[0].GetString()));
        Assert.Equal(previous, await client.GetFromJsonAsync<SettingsBody>("/api/settings"));
    }

    [Fact]
    public async Task Invalid_incoming_put_returns_400_keyed_incomingPath()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings",
            Body("relative/path", _library, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrors(response);
        Assert.True(errors.TryGetProperty("incomingPath", out _));
        Assert.Equal(Fresh, await client.GetFromJsonAsync<SettingsBody>("/api/settings"));
    }

    [Fact]
    public async Task Put_with_unknown_token_returns_400_keyed_directoryTemplate_and_saves_nothing()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings",
            new SettingsBody(_incoming, _library, "%author%/%album%", FileTemplate, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrors(response);
        Assert.True(errors.TryGetProperty("directoryTemplate", out var messages));
        Assert.False(string.IsNullOrWhiteSpace(messages[0].GetString()));
        Assert.Equal(Fresh, await client.GetFromJsonAsync<SettingsBody>("/api/settings"));
    }

    private static async Task<JsonElement> ReadErrors(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").Clone();
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
