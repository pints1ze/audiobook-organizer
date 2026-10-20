using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class PreviewControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private async Task SaveTemplates(HttpClient client, string directoryTemplate, string fileTemplate)
    {
        var response = await client.PutAsJsonAsync("/api/settings", new
        {
            incomingPath = _folders.Incoming, libraryPath = _folders.Library, keepSourceFiles = false,
            directoryTemplate, fileTemplate,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Replaces incoming's root files with two folders of two MP3s each, scans, and names the two books through
    /// PUT /api/groups/{id}. Returns the book ids in the order of <paramref name="titles"/>.
    /// </summary>
    private async Task<List<long>> ScanTwoBooks(HttpClient client, string author, params string[] titles)
    {
        foreach (var folder in new[] { "alpha", "beta" })
        {
            var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, folder)).FullName;
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track1.mp3"));
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        }
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());

        var groups = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        var ids = groups.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(2, ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var renamed = await client.PutAsJsonAsync($"/api/groups/{ids[i]}", new { title = titles[i], author });
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        }
        return ids;
    }

    private static async Task<JsonElement> Preview(HttpClient client)
    {
        var response = await client.GetAsync("/api/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static List<JsonElement> AllFiles(JsonElement preview) => preview.GetProperty("books").EnumerateArray()
        .SelectMany(b => b.GetProperty("files").EnumerateArray()).ToList();

    private static int Count(JsonElement preview, string name) => preview.GetProperty("counts").GetProperty(name).GetInt32();

    [Fact]
    public async Task A_file_template_without_the_track_flags_every_multi_file_book_as_duplicate()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "%album%");
        await ScanTwoBooks(client, "Ann Author", "First Book", "Second Book");

        var preview = await Preview(client);

        Assert.Equal(_folders.Library, preview.GetProperty("libraryPath").GetString());
        var books = preview.GetProperty("books").EnumerateArray().ToList();
        Assert.Equal(2, books.Count);
        Assert.Equal(["First Book", "Second Book"], books.Select(b => b.GetProperty("title").GetString()).Order());
        Assert.All(books, b => Assert.Equal("Ann Author", b.GetProperty("author").GetString()));
        var files = AllFiles(preview);
        Assert.Equal(4, files.Count);
        Assert.All(files, f =>
        {
            Assert.True(f.GetProperty("duplicate").GetBoolean());
            Assert.False(f.GetProperty("exists").GetBoolean());
        });
        Assert.Equal(4, Count(preview, "files"));
        Assert.Equal(4, Count(preview, "duplicates"));
        Assert.Equal(0, Count(preview, "existing"));
        Assert.Equal(0, Count(preview, "missing"));
        Assert.Equal(0, Count(preview, "errors"));
    }

    [Fact]
    public async Task A_file_template_with_the_track_flags_no_duplicates()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "[%track:2% - ]%album%");
        await ScanTwoBooks(client, "Ann Author", "First Book", "Second Book");

        var preview = await Preview(client);

        var files = AllFiles(preview);
        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.False(f.GetProperty("duplicate").GetBoolean()));
        Assert.Equal(0, Count(preview, "duplicates"));
    }

    [Fact]
    public async Task Destinations_that_differ_only_in_letter_case_are_duplicates()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "[%track:2% - ]%album%");
        await ScanTwoBooks(client, "Ann Author", "The Hobbit", "the hobbit");

        var preview = await Preview(client);

        var files = AllFiles(preview);
        var destinations = files.Select(f => f.GetProperty("destinationPath").GetString()!).ToList();
        Assert.Equal(4, destinations.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, f => Assert.True(f.GetProperty("duplicate").GetBoolean()));
        Assert.Equal(4, Count(preview, "duplicates"));
    }

    [Fact]
    public async Task A_file_already_at_a_destination_is_flagged_exists_and_nothing_is_created()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "[%track:2% - ]%album%");
        await ScanTwoBooks(client, "Ann Author", "First Book", "Second Book");
        var target = AllFiles(await Preview(client))[0].GetProperty("destinationPath").GetString()!;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, []);
        var libraryBefore = Directory.GetFileSystemEntries(_folders.Library, "*", SearchOption.AllDirectories).Order().ToList();

        var preview = await Preview(client);

        var files = AllFiles(preview);
        Assert.Equal([target], files.Where(f => f.GetProperty("exists").GetBoolean())
            .Select(f => f.GetProperty("destinationPath").GetString()));
        Assert.Equal(1, Count(preview, "existing"));
        Assert.Equal(libraryBefore, Directory.GetFileSystemEntries(_folders.Library, "*", SearchOption.AllDirectories).Order());
    }

    [Fact]
    public async Task Counts_missing_tokens_and_errors_per_file_and_errored_files_are_never_flagged()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%series%/%album%", "%album%");
        await ScanTwoBooks(client, "Ann Author", "First Book", "Second Book");

        var preview = await Preview(client);

        Assert.Equal(4, Count(preview, "files"));
        Assert.Equal(4, Count(preview, "missing"));
        Assert.Equal(0, Count(preview, "errors"));
        Assert.Equal(4, Count(preview, "duplicates"));

        using (var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("DELETE FROM ApplicationSetting WHERE Key = @key", new { key = SettingsRepository.LibraryPath });
        var unset = await Preview(client);

        Assert.Equal(4, Count(unset, "errors"));
        Assert.Equal(0, Count(unset, "duplicates"));
        Assert.Equal(0, Count(unset, "existing"));
        Assert.All(AllFiles(unset), f => Assert.False(f.GetProperty("duplicate").GetBoolean()));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
