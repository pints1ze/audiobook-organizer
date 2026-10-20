using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class JobsControllerTests : IDisposable
{
    private const string CleanFileTemplate = "[%track:2% - ]%album%";

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

    /// <summary>Two folders of two MP3s each, scanned and named "First Book" and "Second Book" by Ann Author.</summary>
    private async Task<List<long>> ScanTwoBooks(HttpClient client)
    {
        foreach (var folder in new[] { "alpha", "beta" })
        {
            var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, folder)).FullName;
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track1.mp3"));
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        }
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        await Scan(client);

        var ids = await GroupIds(client);
        Assert.Equal(2, ids.Count);
        string[] titles = ["First Book", "Second Book"];
        for (var i = 0; i < ids.Count; i++)
        {
            var renamed = await client.PutAsJsonAsync($"/api/groups/{ids[i]}", new { title = titles[i], author = "Ann Author" });
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        }
        return ids;
    }

    private static async Task Scan(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());
    }

    private static async Task<List<long>> GroupIds(HttpClient client)
    {
        var groups = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        return groups.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetInt64()).ToList();
    }

    private static Task<HttpResponseMessage> Approve(HttpClient client, long groupId) =>
        client.PostAsJsonAsync("/api/jobs", new { groupId });

    private static async Task<JsonElement> BookPreview(HttpClient client, long groupId)
    {
        var response = await client.GetAsync($"/api/groups/{groupId}/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static async Task<List<JsonElement>> Jobs(HttpClient client)
    {
        var response = await client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ScansControllerTests.ReadJson(response)).EnumerateArray().ToList();
    }

    private async Task AssertRefused(HttpClient client, long groupId, string detail)
    {
        var response = await Approve(client, groupId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(detail, (await ScansControllerTests.ReadJson(response)).GetProperty("detail").GetString());
        Assert.Empty(await Jobs(client));
    }

    private List<string> Entries(string root) =>
        Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToList();

    [Fact]
    public async Task Approving_a_clean_book_queues_a_job_with_its_preview_destinations_and_creates_nothing()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var preview = (await BookPreview(client, ids[0])).GetProperty("files").EnumerateArray().ToList();
        var incomingBefore = Entries(_folders.Incoming);

        var response = await Approve(client, ids[0]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ScansControllerTests.ReadJson(response);
        var jobId = job.GetProperty("id").GetInt64();
        Assert.Equal($"/api/jobs/{jobId}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(ids[0], job.GetProperty("groupId").GetInt64());
        Assert.Equal("First Book", job.GetProperty("title").GetString());
        Assert.Equal("Queued", job.GetProperty("status").GetString());
        Assert.True(DateTime.TryParse(job.GetProperty("createdAtUtc").GetString(), out _));
        var files = job.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(preview.Select(f => f.GetProperty("fileId").GetInt64()), files.Select(f => f.GetProperty("fileId").GetInt64()));
        Assert.Equal(preview.Select(f => f.GetProperty("sourcePath").GetString()),
            files.Select(f => f.GetProperty("sourcePath").GetString()));
        Assert.Equal(preview.Select(f => f.GetProperty("destinationPath").GetString()),
            files.Select(f => f.GetProperty("destinationPath").GetString()));
        Assert.All(files, f => Assert.Equal("Pending", f.GetProperty("status").GetString()));

        var listed = Assert.Single(await Jobs(client));
        Assert.Equal(job.GetRawText(), listed.GetRawText());
        var one = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(job.GetRawText(), (await ScansControllerTests.ReadJson(one)).GetRawText());

        Assert.Empty(Entries(_folders.Library));
        Assert.Equal(incomingBefore, Entries(_folders.Incoming));
    }

    [Fact]
    public async Task Jobs_are_listed_newest_first_and_an_unknown_job_is_404()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        Assert.Equal(HttpStatusCode.Created, (await Approve(client, ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Approve(client, ids[1])).StatusCode);

        var jobs = await Jobs(client);

        Assert.Equal([ids[1], ids[0]], jobs.Select(j => j.GetProperty("groupId").GetInt64()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/jobs/9999")).StatusCode);
    }

    [Fact]
    public async Task A_duplicate_target_is_refused_and_no_job_is_recorded()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", "%album%");
        var ids = await ScanTwoBooks(client);

        await AssertRefused(client, ids[0], "Two files would go to the same place: Ann Author/First Book/First Book.mp3.");
    }

    [Fact]
    public async Task An_existing_destination_is_refused_and_no_job_is_recorded()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var second = (await BookPreview(client, ids[0])).GetProperty("files")[1];
        var target = second.GetProperty("destinationPath").GetString()!;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, []);

        await AssertRefused(client, ids[0], $"A file is already at {second.GetProperty("relativePath").GetString()}.");
    }

    [Fact]
    public async Task Missing_values_are_refused_naming_the_tokens()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%series%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);

        await AssertRefused(client, ids[0], "Fill in the missing values: series.");
    }

    [Fact]
    public async Task A_file_with_an_error_is_refused_with_its_error()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        using (var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("DELETE FROM ApplicationSetting WHERE Key = @key", new { key = SettingsRepository.LibraryPath });

        await AssertRefused(client, ids[0], "The library folder isn't set.");
    }

    [Fact]
    public async Task A_book_with_no_files_is_refused()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        using (var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("DELETE FROM FileGroupFile WHERE GroupId = @id", new { id = ids[0] });

        await AssertRefused(client, ids[0], "The book has no files.");
    }

    [Fact]
    public async Task An_unknown_group_is_404_and_a_missing_or_non_positive_group_id_is_400()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Approve(client, 9999)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/jobs", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Approve(client, 0)).StatusCode);
        Assert.Empty(await Jobs(client));
    }

    [Fact]
    public async Task A_second_approval_of_a_queued_book_is_409()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        Assert.Equal(HttpStatusCode.Created, (await Approve(client, ids[0])).StatusCode);

        var again = await Approve(client, ids[0]);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(await Jobs(client));
    }

    [Fact]
    public async Task Approval_marks_the_group_corrected_so_a_rescan_keeps_it_and_its_files()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        // Naming the books corrected them; undo that so only the approval can keep the group.
        using (var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("UPDATE FileGroup SET UserCorrected = 0");
        var filesBefore = (await BookPreview(client, ids[0])).GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("fileId").GetInt64()).ToList();
        Assert.Equal(HttpStatusCode.Created, (await Approve(client, ids[0])).StatusCode);

        await Scan(client);

        var after = await GroupIds(client);
        Assert.Contains(ids[0], after);
        Assert.DoesNotContain(ids[1], after);
        Assert.Equal(filesBefore, (await BookPreview(client, ids[0])).GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("fileId").GetInt64()));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
