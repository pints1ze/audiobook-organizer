using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class JobsControllerTests : IDisposable
{
    private const string CleanFileTemplate = "[%track:2% - ]%album%";

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new() { RunProcessingWorker = true };
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

    /// <summary>
    /// A host on the same database without the processing worker, so approved jobs stay Queued. Starting
    /// <see cref="_factory"/> afterwards is the "restart": its worker drains the queue when it starts.
    /// </summary>
    private WebApplicationFactory<Program> QueueOnly() => _factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(AudiobookOrganizerWebApplicationFactory.RemoveProcessingWorker));

    /// <summary>Polls GET /api/jobs/{id} until the job is Succeeded or Failed, failing after 10 seconds.</summary>
    private static async Task<JsonElement> WaitForJob(HttpClient client, long jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var response = await client.GetAsync($"/api/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var job = await ScansControllerTests.ReadJson(response);
            var status = job.GetProperty("status").GetString();
            if (status is "Succeeded" or "Failed") return job;
            Assert.True(DateTime.UtcNow < deadline, $"Job {jobId} was still {status} after 10 seconds.");
            await Task.Delay(50);
        }
    }

    private static async Task<JsonElement> ApproveJob(HttpClient client, long groupId)
    {
        var response = await Approve(client, groupId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static List<string> Destinations(JsonElement job) => job.GetProperty("files").EnumerateArray()
        .Select(f => f.GetProperty("destinationPath").GetString()!).ToList();

    /// <summary>Every file under the root with its bytes' hash and last write time.</summary>
    private static List<string> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order()
            .Select(p => $"{p}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}|{File.GetLastWriteTimeUtc(p):O}")
            .ToList();

    private List<string> PartialFiles() =>
        Directory.GetFiles(_folders.Library, "*.partial", SearchOption.AllDirectories).ToList();

    [Fact]
    public async Task Approving_a_clean_book_queues_a_job_with_its_preview_destinations_and_creates_nothing()
    {
        using var host = QueueOnly();
        var client = host.CreateClient();
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
        using var host = QueueOnly();
        var client = host.CreateClient();
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
        using var host = QueueOnly();
        var client = host.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        // Naming the books corrected them; undo that so only the approval can keep the group.
        using (var connection = host.Services.GetRequiredService<SqliteConnectionFactory>().Open())
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

    [Fact]
    public async Task An_approved_book_is_copied_to_its_destinations_and_the_job_succeeds()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var incomingBefore = Snapshot(_folders.Incoming);

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("error").ValueKind);
        Assert.True(DateTime.TryParse(job.GetProperty("startedAtUtc").GetString(), out _));
        Assert.True(DateTime.TryParse(job.GetProperty("finishedAtUtc").GetString(), out _));
        var files = job.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        foreach (var file in files)
        {
            Assert.Equal("Copied", file.GetProperty("status").GetString());
            Assert.Equal(File.ReadAllBytes(file.GetProperty("sourcePath").GetString()!),
                File.ReadAllBytes(file.GetProperty("destinationPath").GetString()!));
        }
        Assert.Empty(PartialFiles());
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_file_already_at_a_destination_is_never_overwritten_and_the_jobs_copies_are_removed()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var destinations = Destinations(created);
        Directory.CreateDirectory(Path.GetDirectoryName(destinations[1])!);
        File.WriteAllText(destinations[1], "someone else's file");
        var incomingBefore = Snapshot(_folders.Incoming);

        var job = await WaitForJob(_factory.CreateClient(), created.GetProperty("id").GetInt64());

        var message = $"A file is already at {destinations[1]}.";
        Assert.Equal(("Failed", message), (job.GetProperty("status").GetString(), job.GetProperty("error").GetString()));
        Assert.True(DateTime.TryParse(job.GetProperty("finishedAtUtc").GetString(), out _));
        var failed = job.GetProperty("files")[1];
        Assert.Equal(("Failed", message), (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.Equal("someone else's file", File.ReadAllText(destinations[1]));
        Assert.False(File.Exists(destinations[0]));
        Assert.Empty(PartialFiles());
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_failed_job_does_not_stop_the_next_queued_job()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var first = await ApproveJob(setup, ids[0]);
        var second = await ApproveJob(setup, ids[1]);
        var blocked = Destinations(first)[1];
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "someone else's file");
        var incomingBefore = Snapshot(_folders.Incoming);

        var client = _factory.CreateClient();
        var failedJob = await WaitForJob(client, first.GetProperty("id").GetInt64());
        var nextJob = await WaitForJob(client, second.GetProperty("id").GetInt64());

        Assert.Equal("Failed", failedJob.GetProperty("status").GetString());
        Assert.Equal("Succeeded", nextJob.GetProperty("status").GetString());
        Assert.All(Destinations(nextJob), d => Assert.True(File.Exists(d)));
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_source_gone_after_approval_fails_the_job()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var gone = created.GetProperty("files")[1].GetProperty("sourcePath").GetString()!;
        File.Delete(gone);

        var job = await WaitForJob(_factory.CreateClient(), created.GetProperty("id").GetInt64());

        Assert.Equal(("Failed", $"The source file is gone: {gone}."),
            (job.GetProperty("status").GetString(), job.GetProperty("error").GetString()));
        Assert.All(Destinations(created), d => Assert.False(File.Exists(d)));
        Assert.Empty(PartialFiles());
    }

    [Fact]
    public async Task At_startup_a_running_job_is_failed_as_interrupted_and_queued_jobs_run()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var interrupted = await ApproveJob(setup, ids[0]);
        var queued = await ApproveJob(setup, ids[1]);
        using (var connection = host.Services.GetRequiredService<SqliteConnectionFactory>().Open())
            connection.Execute("UPDATE ProcessingJob SET Status = 'Running' WHERE Id = @id",
                new { id = interrupted.GetProperty("id").GetInt64() });

        var client = _factory.CreateClient();
        var failed = await WaitForJob(client, interrupted.GetProperty("id").GetInt64());
        var ran = await WaitForJob(client, queued.GetProperty("id").GetInt64());

        Assert.Equal(("Failed", "Interrupted by an application restart."),
            (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.Equal("Succeeded", ran.GetProperty("status").GetString());
    }

    [Fact]
    public void FailInterruptedJobs_fails_only_running_jobs()
    {
        using var host = QueueOnly();
        var repository = host.Services.GetRequiredService<ProcessingRepository>();
        long Insert(string status, string? finishedAt, string? error)
        {
            var id = repository.CreateJob(
                new ProcessingJob { Title = status, Status = ProcessingJob.Queued, CreatedAtUtc = "2026-10-19T00:00:00.0000000Z" }, []);
            var job = repository.GetJob(id)!.Value.Job;
            job.Status = status;
            job.FinishedAtUtc = finishedAt;
            job.Error = error;
            repository.UpdateJob(job);
            return id;
        }
        var queued = Insert(ProcessingJob.Queued, null, null);
        var running = Insert(ProcessingJob.Running, null, null);
        var succeeded = Insert(ProcessingJob.Succeeded, "2026-10-19T00:30:00.0000000Z", null);
        var failed = Insert(ProcessingJob.Failed, "2026-10-19T00:30:00.0000000Z", "Earlier failure.");

        repository.FailInterruptedJobs("Interrupted by an application restart.", "2026-10-19T01:00:00.0000000Z");

        (string, string?, string?) Read(long id)
        {
            var job = repository.GetJob(id)!.Value.Job;
            return (job.Status, job.Error, job.FinishedAtUtc);
        }
        Assert.Equal(("Failed", "Interrupted by an application restart.", "2026-10-19T01:00:00.0000000Z"), Read(running));
        Assert.Equal(("Queued", null, null), Read(queued));
        Assert.Equal(("Succeeded", null, "2026-10-19T00:30:00.0000000Z"), Read(succeeded));
        Assert.Equal(("Failed", "Earlier failure.", "2026-10-19T00:30:00.0000000Z"), Read(failed));
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
