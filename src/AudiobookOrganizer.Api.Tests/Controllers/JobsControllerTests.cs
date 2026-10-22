using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services;
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

    private async Task SaveTemplates(HttpClient client, string directoryTemplate, string fileTemplate, bool keepSourceFiles = false)
    {
        var response = await client.PutAsJsonAsync("/api/settings", new
        {
            incomingPath = _folders.Incoming, libraryPath = _folders.Library, keepSourceFiles,
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

    private static List<string> Sources(JsonElement job) => job.GetProperty("files").EnumerateArray()
        .Select(f => f.GetProperty("sourcePath").GetString()!).ToList();

    private static List<string> Hashes(IEnumerable<string> paths) =>
        paths.Select(p => $"{p}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}").ToList();

    /// <summary>
    /// A host whose reader passes each read of a file under the library through <paramref name="adjust"/> (path, what
    /// was read, how many library reads so far including this one); reads of incoming files are the real ones.
    /// </summary>
    private WebApplicationFactory<Program> ReadBack(Func<string, AudioFileMetadata, int, AudioFileMetadata> adjust)
    {
        var reads = 0;
        return _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<AudioFileReader>(provider => new AdjustingAudioFileReader(
                provider.GetRequiredService<ChapterReader>(), provider.GetRequiredService<FfmpegRunner>(),
                (path, read) => path.StartsWith(_folders.Library, StringComparison.OrdinalIgnoreCase)
                    ? adjust(path, read, Interlocked.Increment(ref reads))
                    : read))));
    }

    /// <summary>The source paths of a book, in book order.</summary>
    private static async Task<List<string>> BookSources(HttpClient client, long groupId) =>
        (await BookPreview(client, groupId)).GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("sourcePath").GetString()!).ToList();

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
    public async Task With_keep_source_on_an_approved_book_is_copied_tagged_and_verified_and_every_source_stays()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true);
        var ids = await ScanTwoBooks(client);
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(TagRoundTripTests.CoverJpeg);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "file", "cover.jpg");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/groups/{ids[0]}/cover/upload", form)).StatusCode);
        var incomingBefore = Snapshot(_folders.Incoming);

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("error").ValueKind);
        Assert.True(DateTime.TryParse(job.GetProperty("startedAtUtc").GetString(), out _));
        Assert.True(DateTime.TryParse(job.GetProperty("finishedAtUtc").GetString(), out _));
        var files = job.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            Assert.Equal("Copied", files[i].GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, files[i].GetProperty("error").ValueKind);
            var source = files[i].GetProperty("sourcePath").GetString()!;
            var destination = files[i].GetProperty("destinationPath").GetString()!;
            Assert.Equal(TagRoundTripTests.Mp3PayloadHash(source), TagRoundTripTests.Mp3PayloadHash(destination));
            using var tagged = TagLib.File.Create(destination);
            Assert.Equal(("First Book", "First Book"), (tagged.Tag.Title, tagged.Tag.Album));
            Assert.Equal(["Ann Author"], tagged.Tag.Performers);
            Assert.Equal(["Ann Author"], tagged.Tag.AlbumArtists);
            Assert.Equal(((uint)i + 1, 0u), (tagged.Tag.Track, tagged.Tag.TrackCount));
            Assert.Equal(TagRoundTripTests.CoverJpeg, Assert.Single(tagged.Tag.Pictures).Data.Data);
        }
        Assert.Empty(PartialFiles());
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_processed_books_copies_get_its_narrator_series_and_asin()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true);
        var ids = await ScanTwoBooks(client);
        var saved = await client.PutAsJsonAsync($"/api/groups/{ids[0]}/metadata", new
        {
            fields = new { narrators = new[] { "Nora Narrator" }, series = "The Saga", seriesPart = "3", asin = "B0TESTASIN" },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        foreach (var destination in Destinations(job))
        {
            using var tagged = TagLib.File.Create(destination);
            var id3 = (TagLib.Id3v2.Tag)tagged.GetTag(TagLib.TagTypes.Id3v2, false);
            Assert.Equal(["Nora Narrator"], tagged.Tag.Composers);
            Assert.Equal("The Saga", TagLib.Id3v2.UserTextInformationFrame.Get(id3, "SERIES", false)?.Text.Single());
            Assert.Equal("B0TESTASIN", TagLib.Id3v2.UserTextInformationFrame.Get(id3, "ASIN", false)?.Text.Single());
            Assert.Equal("The Saga, Book #3", tagged.Tag.Grouping);
        }
    }

    [Fact]
    public async Task A_job_whose_book_was_merged_away_after_approval_fails_and_writes_nothing()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var fileIds = created.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("fileId").GetInt64()).ToList();
        Assert.Equal(HttpStatusCode.OK, (await setup.PostAsJsonAsync($"/api/groups/{ids[1]}/files", new { fileIds })).StatusCode);
        Assert.DoesNotContain(ids[0], await GroupIds(setup));
        var incomingBefore = Snapshot(_folders.Incoming);

        var job = await WaitForJob(_factory.CreateClient(), created.GetProperty("id").GetInt64());

        Assert.Equal(("Failed", "The book is no longer in the library."),
            (job.GetProperty("status").GetString(), job.GetProperty("error").GetString()));
        Assert.All(job.GetProperty("files").EnumerateArray(), f => Assert.Equal("Pending", f.GetProperty("status").GetString()));
        Assert.Empty(Entries(_folders.Library));
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_failed_tag_write_fails_the_file_and_job_and_removes_the_jobs_copies()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        // Not audio any more, so the tag write on the second file fails after the first copy is in place.
        File.WriteAllBytes(created.GetProperty("files")[1].GetProperty("sourcePath").GetString()!, new byte[4096]);
        var incomingBefore = Snapshot(_folders.Incoming);

        var job = await WaitForJob(_factory.CreateClient(), created.GetProperty("id").GetInt64());

        Assert.Equal("Failed", job.GetProperty("status").GetString());
        var failed = job.GetProperty("files")[1];
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("error").GetString()));
        Assert.Equal(job.GetProperty("error").GetString(), failed.GetProperty("error").GetString());
        Assert.Equal("Pending", job.GetProperty("files")[0].GetProperty("status").GetString());
        Assert.All(Destinations(created), d => Assert.False(File.Exists(d)));
        Assert.Empty(PartialFiles());
        // Keep-source is off, but a failed job deletes no source.
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task With_keep_source_off_a_verified_job_deletes_every_source_and_its_files_end_SourceDeleted()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var otherBook = await BookSources(client, ids[1]);

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal(("Succeeded", JsonValueKind.Null), (job.GetProperty("status").GetString(), job.GetProperty("error").ValueKind));
        var files = job.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            Assert.Equal(("SourceDeleted", JsonValueKind.Null),
                (files[i].GetProperty("status").GetString(), files[i].GetProperty("error").ValueKind));
            Assert.False(File.Exists(files[i].GetProperty("sourcePath").GetString()));
            using var tagged = TagLib.File.Create(files[i].GetProperty("destinationPath").GetString()!);
            Assert.Equal(("First Book", (uint)i + 1), (tagged.Tag.Title, tagged.Tag.Track));
        }
        Assert.All(otherBook, s => Assert.True(File.Exists(s)));
        Assert.Empty(PartialFiles());
    }

    [Fact]
    public async Task An_m4b_book_with_chapters_processes_to_Succeeded_its_copy_has_no_chapters_and_its_source_is_deleted()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "m4b-book")).FullName;
        var source = Path.Combine(book, "book.m4b");
        File.Copy(ChapterReaderTests.Fixture("chapters.m4b"), source);
        Assert.NotEmpty(ChapterReaderTests.NewReader().Read(source));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        await Scan(client);
        var groupId = Assert.Single(await GroupIds(client));
        var renamed = await client.PutAsJsonAsync($"/api/groups/{groupId}", new { title = "Chaptered Book", author = "Ann Author" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var created = await ApproveJob(client, groupId);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal(("Succeeded", JsonValueKind.Null), (job.GetProperty("status").GetString(), job.GetProperty("error").ValueKind));
        var file = Assert.Single(job.GetProperty("files").EnumerateArray());
        Assert.Equal("SourceDeleted", file.GetProperty("status").GetString());
        Assert.False(File.Exists(source));
        var destination = file.GetProperty("destinationPath").GetString()!;
        Assert.True(File.Exists(destination));
        Assert.Empty(ChapterReaderTests.NewReader().Read(destination));
        Assert.Empty(PartialFiles());
    }

    [Fact]
    public async Task A_failed_read_back_fails_the_job_naming_the_check_and_leaves_no_copy_and_every_source()
    {
        var realChapters = -1;
        using var host = ReadBack((_, read, n) =>
        {
            if (n != 2) return read;
            realChapters = read.ChapterCount;
            return read with { ChapterCount = read.ChapterCount - 1 };
        });
        var client = host.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var incomingBefore = Snapshot(_folders.Incoming);

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.True(realChapters > 1, "The fixture should have more than one chapter.");
        var copied = realChapters - 1;
        var message = $"The copy of {Path.GetFileName(Destinations(created)[1])} has {copied} chapter{(copied == 1 ? "" : "s")}; " +
            $"the source has {realChapters}.";
        Assert.Equal(("Failed", message), (job.GetProperty("status").GetString(), job.GetProperty("error").GetString()));
        var failed = job.GetProperty("files")[1];
        Assert.Equal(("Failed", message), (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.Equal("Pending", job.GetProperty("files")[0].GetProperty("status").GetString());
        Assert.All(Destinations(created), d => Assert.False(File.Exists(d)));
        Assert.Empty(PartialFiles());
        Assert.Equal(incomingBefore, Snapshot(_folders.Incoming));
    }

    [Fact]
    public async Task A_source_changed_after_its_copy_is_kept_with_an_error_and_the_job_still_succeeds()
    {
        string? touched = null;
        using var host = ReadBack((_, read, n) =>
        {
            if (n == 2) File.SetLastWriteTimeUtc(touched!, File.GetLastWriteTimeUtc(touched!).AddMinutes(5));
            return read;
        });
        var client = host.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var sources = await BookSources(client, ids[0]);
        touched = sources[0];

        var created = await ApproveJob(client, ids[0]);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal(("Succeeded", JsonValueKind.Null), (job.GetProperty("status").GetString(), job.GetProperty("error").ValueKind));
        var files = job.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(("Copied", "The source changed after it was copied, so it was kept."),
            (files[0].GetProperty("status").GetString(), files[0].GetProperty("error").GetString()));
        Assert.Equal("SourceDeleted", files[1].GetProperty("status").GetString());
        Assert.True(File.Exists(sources[0]));
        Assert.False(File.Exists(sources[1]));
        Assert.All(Destinations(job), d => Assert.True(File.Exists(d)));
    }

    [Fact]
    public async Task A_source_that_cant_be_deleted_is_kept_with_an_error_and_the_job_still_succeeds()
    {
        // An open handle without delete sharing blocks File.Delete only on Windows, where the tests run.
        if (!OperatingSystem.IsWindows()) return;
        string? locked = null;
        FileStream? hold = null;
        using var host = ReadBack((_, read, n) =>
        {
            if (n == 2) hold = new FileStream(locked!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return read;
        });
        var client = host.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var sources = await BookSources(client, ids[0]);
        locked = sources[0];

        JsonElement job;
        try
        {
            var created = await ApproveJob(client, ids[0]);
            job = await WaitForJob(client, created.GetProperty("id").GetInt64());
        }
        finally
        {
            hold?.Dispose();
        }

        Assert.Equal(("Succeeded", JsonValueKind.Null), (job.GetProperty("status").GetString(), job.GetProperty("error").ValueKind));
        var first = job.GetProperty("files")[0];
        Assert.Equal("Copied", first.GetProperty("status").GetString());
        Assert.StartsWith("The copy is in place, but the source couldn't be deleted: ", first.GetProperty("error").GetString());
        Assert.Equal("SourceDeleted", job.GetProperty("files")[1].GetProperty("status").GetString());
        Assert.True(File.Exists(sources[0]));
        Assert.All(Destinations(job), d => Assert.True(File.Exists(d)));
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
    public async Task A_failed_book_retried_by_approving_again_ends_with_one_copy_of_each_file_and_keeps_the_failed_job()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var first = await ApproveJob(setup, ids[0]);
        var destinations = Destinations(first);
        Directory.CreateDirectory(Path.GetDirectoryName(destinations[1])!);
        File.WriteAllText(destinations[1], "someone else's file");
        var client = _factory.CreateClient();
        var failed = await WaitForJob(client, first.GetProperty("id").GetInt64());
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        File.Delete(destinations[1]);

        var retried = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", retried.GetProperty("status").GetString());
        Assert.Equal(destinations, Destinations(retried));
        Assert.Equal(destinations.Order(), Directory.GetFiles(_folders.Library, "*", SearchOption.AllDirectories).Order());
        Assert.Empty(PartialFiles());
        var jobs = await Jobs(client);
        Assert.Equal(2, jobs.Count);
        var history = jobs[1];
        Assert.Equal(first.GetProperty("id").GetInt64(), history.GetProperty("id").GetInt64());
        Assert.Equal(("Failed", $"A file is already at {destinations[1]}."),
            (history.GetProperty("status").GetString(), history.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Approving_a_book_whose_job_succeeded_is_refused_so_it_is_never_copied_twice()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true);
        var ids = await ScanTwoBooks(client);
        var firstFile = (await BookPreview(client, ids[0])).GetProperty("files")[0].GetProperty("relativePath").GetString();
        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        var libraryBefore = Snapshot(_folders.Library);

        var again = await Approve(client, ids[0]);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal($"A file is already at {firstFile}.",
            (await ScansControllerTests.ReadJson(again)).GetProperty("detail").GetString());
        Assert.Single(await Jobs(client));
        Assert.Equal(libraryBefore, Snapshot(_folders.Library));
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
        var firstSourcesBefore = Hashes(Sources(first));

        var client = _factory.CreateClient();
        var failedJob = await WaitForJob(client, first.GetProperty("id").GetInt64());
        var nextJob = await WaitForJob(client, second.GetProperty("id").GetInt64());

        Assert.Equal("Failed", failedJob.GetProperty("status").GetString());
        Assert.Equal("Succeeded", nextJob.GetProperty("status").GetString());
        Assert.All(Destinations(nextJob), d => Assert.True(File.Exists(d)));
        Assert.Equal(firstSourcesBefore, Hashes(Sources(first)));
        Assert.All(Sources(nextJob), s => Assert.False(File.Exists(s)));
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
