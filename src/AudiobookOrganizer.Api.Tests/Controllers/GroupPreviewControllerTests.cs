using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupPreviewControllerTests : IDisposable
{
    private const string Title = "Preview Book";

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
    /// Scans a two-file "book" folder (plus the root M4B), titles the book and gives it the author "Author, Ann", an
    /// alias of the person "Ann Author". Returns the book's id and its file ids in book order.
    /// </summary>
    private async Task<(long Id, List<long> FileIds)> ScanTwoFileBook(HttpClient client)
    {
        var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "book")).FullName;
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(book, "track1.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());

        var groups = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        var id = groups.GetProperty("groups").EnumerateArray()
            .Single(g => g.GetProperty("fileCount").GetInt32() == 2).GetProperty("id").GetInt64();

        var metadata = await client.PutAsJsonAsync($"/api/groups/{id}/metadata",
            new { fields = new { title = Title, authors = new[] { "Author, Ann" } } });
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        var person = await client.PostAsJsonAsync("/api/people", new { name = "Ann Author", aliases = new[] { "Author, Ann" } });
        Assert.True(person.IsSuccessStatusCode, await person.Content.ReadAsStringAsync());

        var detail = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{id}"));
        return (id, detail.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("fileId").GetInt64()).ToList());
    }

    private static async Task<JsonElement> Preview(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ScansControllerTests.ReadJson(response);
        Assert.Equal(id, body.GetProperty("groupId").GetInt64());
        return body;
    }

    private static List<JsonElement> Files(JsonElement preview) => preview.GetProperty("files").EnumerateArray().ToList();

    private static List<string?> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()).ToList();

    [Fact]
    public async Task Unknown_group_returns_404()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/preview")).StatusCode);
    }

    [Fact]
    public async Task Previews_each_file_in_book_order_with_the_canonical_author_under_the_library()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "[%track:2% - ]%title%");
        var (id, fileIds) = await ScanTwoFileBook(client);
        var libraryBefore = Directory.GetFileSystemEntries(_folders.Library);

        var preview = await Preview(client, id);

        Assert.Equal(_folders.Library, preview.GetProperty("libraryPath").GetString());
        var files = Files(preview);
        Assert.Equal(fileIds, files.Select(f => f.GetProperty("fileId").GetInt64()));
        Assert.Equal([$"Ann Author/{Title}/01 - {Title}.mp3", $"Ann Author/{Title}/02 - {Title}.mp3"],
            files.Select(f => f.GetProperty("relativePath").GetString()));
        Assert.Equal(
            [Path.Combine(_folders.Library, "Ann Author", Title, $"01 - {Title}.mp3"),
             Path.Combine(_folders.Library, "Ann Author", Title, $"02 - {Title}.mp3")],
            files.Select(f => f.GetProperty("destinationPath").GetString()));
        Assert.Equal([Path.Combine(_folders.Incoming, "book", "track1.mp3"), Path.Combine(_folders.Incoming, "book", "track2.mp3")],
            files.Select(f => f.GetProperty("sourcePath").GetString()));
        Assert.All(files, f =>
        {
            Assert.Empty(f.GetProperty("missing").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, f.GetProperty("error").ValueKind);
            Assert.False(f.GetProperty("duplicate").GetBoolean());
            Assert.False(f.GetProperty("exists").GetBoolean());
        });
        Assert.Equal(libraryBefore, Directory.GetFileSystemEntries(_folders.Library));
    }

    [Fact]
    public async Task A_missing_series_is_listed_for_every_file_and_a_reorder_swaps_the_track_numbers()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%series%/%album%", "%track:2% - %title% (%artist%)");
        var (id, fileIds) = await ScanTwoFileBook(client);

        var before = Files(await Preview(client, id));
        Assert.All(before, f => Assert.Equal(["series"], Strings(f.GetProperty("missing"))));
        Assert.Equal($"Ann Author/{Title}/01 - {Title} (Ann Author).mp3", before[0].GetProperty("relativePath").GetString());

        fileIds.Reverse();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/order", new { fileIds })).StatusCode);
        var after = Files(await Preview(client, id));

        Assert.Equal(fileIds, after.Select(f => f.GetProperty("fileId").GetInt64()));
        Assert.Equal([$"Ann Author/{Title}/01 - {Title} (Ann Author).mp3", $"Ann Author/{Title}/02 - {Title} (Ann Author).mp3"],
            after.Select(f => f.GetProperty("relativePath").GetString()));
        Assert.Equal(before[0].GetProperty("sourcePath").GetString(), after[1].GetProperty("sourcePath").GetString());
    }

    [Fact]
    public async Task With_no_library_path_each_file_has_an_error_and_null_paths()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "%title%");
        var (id, _) = await ScanTwoFileBook(client);
        using (var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("DELETE FROM ApplicationSetting WHERE Key = @key", new { key = SettingsRepository.LibraryPath });

        var preview = await Preview(client, id);

        Assert.Equal(JsonValueKind.Null, preview.GetProperty("libraryPath").ValueKind);
        var files = Files(preview);
        Assert.Equal(2, files.Count);
        Assert.All(files, f =>
        {
            Assert.Equal("The library folder isn't set.", f.GetProperty("error").GetString());
            Assert.Equal(JsonValueKind.Null, f.GetProperty("relativePath").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("destinationPath").ValueKind);
        });
    }

    [Fact]
    public async Task A_file_that_collides_with_another_book_is_flagged_duplicate()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "[%track:2% - ]%title%");
        var other = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "other")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(other, "only.mp3"));
        var (id, fileIds) = await ScanTwoFileBook(client);
        var otherSource = Path.Combine(other, "only.mp3");
        var otherId = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/preview")))
            .GetProperty("books").EnumerateArray()
            .Single(b => b.GetProperty("files").EnumerateArray().Any(f => f.GetProperty("sourcePath").GetString() == otherSource))
            .GetProperty("groupId").GetInt64();
        var renamed = await client.PutAsJsonAsync($"/api/groups/{otherId}", new { title = Title, author = "Author, Ann" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var files = Files(await Preview(client, id));

        Assert.Equal(fileIds, files.Select(f => f.GetProperty("fileId").GetInt64()));
        Assert.Equal([true, false], files.Select(f => f.GetProperty("duplicate").GetBoolean()));
        var otherFile = Assert.Single(Files(await Preview(client, otherId)));
        Assert.True(otherFile.GetProperty("duplicate").GetBoolean());
        Assert.Equal(files[0].GetProperty("destinationPath").GetString(), otherFile.GetProperty("destinationPath").GetString());
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
