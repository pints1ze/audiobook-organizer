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

    private static async Task<JsonElement> Scan(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var run = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", run.GetProperty("status").GetString());
        return run;
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
    public async Task A_book_of_an_mp3_TagLibSharp_cannot_parse_processes_to_Succeeded_tagged_and_its_source_is_deleted()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "broken-book")).FullName;
        var source = Path.Combine(book, "book.mp3");
        AudioFileReaderTests.WriteBrokenMp3(source);
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        await Scan(client);
        var groupId = Assert.Single(await GroupIds(client));
        var renamed = await client.PutAsJsonAsync($"/api/groups/{groupId}", new { title = "Broken Book", author = "Ann Author" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var created = await ApproveJob(client, groupId);
        var job = await WaitForJob(client, created.GetProperty("id").GetInt64());

        Assert.Equal(("Succeeded", JsonValueKind.Null), (job.GetProperty("status").GetString(), job.GetProperty("error").ValueKind));
        var file = Assert.Single(job.GetProperty("files").EnumerateArray());
        Assert.Equal("SourceDeleted", file.GetProperty("status").GetString());
        Assert.False(File.Exists(source));
        var destination = file.GetProperty("destinationPath").GetString()!;
        using (var tagged = TagLib.File.Create(destination))
            Assert.Equal(("Broken Book", "Ann Author", 1u), (tagged.Tag.Title, tagged.Tag.FirstPerformer, tagged.Tag.Track));
        Assert.Equal(3, ChapterReaderTests.NewReader().Read(destination).Count);
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
        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        var libraryBefore = Snapshot(_folders.Library);

        var again = await Approve(client, ids[0]);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(ProcessingService.AlreadyProcessed,
            (await ScansControllerTests.ReadJson(again)).GetProperty("detail").GetString());
        Assert.Single(await Jobs(client));
        Assert.Equal(libraryBefore, Snapshot(_folders.Library));
    }

    [Fact]
    public async Task A_processed_book_leaves_the_groups_list_the_preview_and_the_dashboard_counts_but_its_book_page_stays()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var dashboardBefore = await ScansControllerTests.ReadJson(await client.GetAsync("/api/dashboard"));
        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", done.GetProperty("status").GetString());

        Assert.Equal([ids[1]], await GroupIds(client));
        var preview = await ScansControllerTests.ReadJson(await client.GetAsync("/api/preview"));
        Assert.Equal([ids[1]], preview.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("groupId").GetInt64()));
        Assert.Equal(2, preview.GetProperty("counts").GetProperty("files").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{ids[0]}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/groups/{ids[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/groups/{ids[0]}/metadata")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/groups/{ids[0]}/cover")).StatusCode);
        Assert.Equal(done.GetProperty("id").GetInt64(), Assert.Single(await Jobs(client)).GetProperty("id").GetInt64());
        var dashboard = await ScansControllerTests.ReadJson(await client.GetAsync("/api/dashboard"));
        Assert.Equal(2, dashboardBefore.GetProperty("bookCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("bookCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("correctedBookCount").GetInt32());
        var byConfidence = dashboard.GetProperty("booksByConfidence");
        Assert.Equal(1, byConfidence.GetProperty("low").GetInt32() + byConfidence.GetProperty("medium").GetInt32()
            + byConfidence.GetProperty("high").GetInt32());
    }

    [Fact]
    public async Task A_book_whose_only_job_failed_is_still_listed_and_previewed()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var destinations = Destinations(created);
        Directory.CreateDirectory(Path.GetDirectoryName(destinations[1])!);
        File.WriteAllText(destinations[1], "someone else's file");
        var client = _factory.CreateClient();
        Assert.Equal("Failed", (await WaitForJob(client, created.GetProperty("id").GetInt64())).GetProperty("status").GetString());

        Assert.Equal(ids.Order(), (await GroupIds(client)).Order());
        var preview = await ScansControllerTests.ReadJson(await client.GetAsync("/api/preview"));
        Assert.Equal(ids.Order(), preview.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("groupId").GetInt64()).Order());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/groups/{ids[0]}/preview")).StatusCode);
    }

    /// <summary>The groups' ids plus the unresolved and excluded file ids, as GET /api/groups lists them.</summary>
    private static async Task<(List<long> Groups, List<long> Unresolved, List<long> Excluded)> GroupList(HttpClient client)
    {
        var list = await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"));
        List<long> Ids(string name, string id) => list.GetProperty(name).EnumerateArray().Select(e => e.GetProperty(id).GetInt64()).ToList();
        return (Ids("groups", "id"), Ids("unresolved", "fileId"), Ids("excluded", "fileId"));
    }

    [Fact]
    public async Task After_processing_with_keep_source_off_a_rescan_keeps_the_groups_and_the_book_cant_be_processed_again()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        Assert.All(Sources(done), s => Assert.False(File.Exists(s)));
        var listBefore = await GroupList(client);
        Assert.Empty(listBefore.Unresolved);
        Assert.Empty(listBefore.Excluded);
        var libraryBefore = Directory.GetFiles(_folders.Library, "*", SearchOption.AllDirectories).Order().ToList();
        Assert.Equal(Destinations(done).Order(), libraryBefore);

        var rescan = await Scan(client);

        Assert.Equal(0, rescan.GetProperty("filesMissing").GetInt32());
        var listAfter = await GroupList(client);
        Assert.Equal([ids[1]], listAfter.Groups);
        Assert.Empty(listAfter.Unresolved);
        Assert.Empty(listAfter.Excluded);
        var again = await Approve(client, ids[0]);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(ProcessingService.AlreadyProcessed,
            (await ScansControllerTests.ReadJson(again)).GetProperty("detail").GetString());
        Assert.Single(await Jobs(client));
        Assert.Equal(libraryBefore, Directory.GetFiles(_folders.Library, "*", SearchOption.AllDirectories).Order());

        // The untouched second book still processes normally after the rescan.
        var second = await WaitForJob(client, (await ApproveJob(client, ids[1])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", second.GetProperty("status").GetString());
        Assert.All(second.GetProperty("files").EnumerateArray(), f => Assert.Equal("SourceDeleted", f.GetProperty("status").GetString()));
        Assert.Equal(libraryBefore.Concat(Destinations(second)).Order(),
            Directory.GetFiles(_folders.Library, "*", SearchOption.AllDirectories).Order());
    }

    [Fact]
    public async Task After_processing_with_keep_source_on_a_rescan_finds_the_sources_unchanged_and_leaves_groups_and_library_alone()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true);
        var ids = await ScanTwoBooks(client);
        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        var listBefore = await GroupList(client);
        Assert.Empty(listBefore.Unresolved);
        Assert.Empty(listBefore.Excluded);
        var libraryBefore = Snapshot(_folders.Library);
        Assert.Equal(Destinations(done).Order(), libraryBefore.Select(e => e.Split('|')[0]));

        var rescan = await Scan(client);

        Assert.All(Sources(done), s => Assert.True(File.Exists(s)));
        // All four sources (book 1's included) are unchanged; none is new, updated or missing.
        Assert.Equal((4, 0, 0, 0), (rescan.GetProperty("filesUnchanged").GetInt32(), rescan.GetProperty("filesAdded").GetInt32(),
            rescan.GetProperty("filesUpdated").GetInt32(), rescan.GetProperty("filesMissing").GetInt32()));
        var listAfter = await GroupList(client);
        Assert.Equal([ids[1]], listAfter.Groups);
        Assert.Empty(listAfter.Unresolved);
        Assert.Empty(listAfter.Excluded);
        var again = await Approve(client, ids[0]);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(ProcessingService.AlreadyProcessed,
            (await ScansControllerTests.ReadJson(again)).GetProperty("detail").GetString());
        Assert.Single(await Jobs(client));
        Assert.Equal(libraryBefore, Snapshot(_folders.Library));

        // The untouched second book still processes normally after the rescan.
        var second = await WaitForJob(client, (await ApproveJob(client, ids[1])).GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", second.GetProperty("status").GetString());
        Assert.All(second.GetProperty("files").EnumerateArray(), f => Assert.Equal("Copied", f.GetProperty("status").GetString()));
        Assert.Equal(libraryBefore.Select(e => e.Split('|')[0]).Concat(Destinations(second)).Order(),
            Directory.GetFiles(_folders.Library, "*", SearchOption.AllDirectories).Order());
    }

    /// <summary>How many AudioFile and FileGroupFile rows the files have.</summary>
    private (long Files, long Members) FileRows(IReadOnlyList<long> fileIds)
    {
        using var connection = _factory.Services.GetRequiredService<SqliteConnectionFactory>().Open();
        return (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM AudioFile WHERE Id IN @fileIds", new { fileIds }),
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM FileGroupFile WHERE AudioFileId IN @fileIds", new { fileIds }));
    }

    private static List<long> FileIds(JsonElement job) => job.GetProperty("files").EnumerateArray()
        .Select(f => f.GetProperty("fileId").GetInt64()).ToList();

    [Fact]
    public async Task With_keep_source_off_a_processed_books_rows_and_emptied_folder_are_removed_and_its_job_history_stays()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);

        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        Assert.Equal((0L, 0L), FileRows(FileIds(done)));
        var listed = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/jobs/{done.GetProperty("id").GetInt64()}"));
        Assert.Equal(["SourceDeleted", "SourceDeleted"], FileStatuses(listed));
        Assert.Equal(Sources(done), Sources(listed));
        Assert.False(Directory.Exists(Path.Combine(_folders.Incoming, "alpha")));
        Assert.True(Directory.Exists(Path.Combine(_folders.Incoming, "beta")));
        Assert.True(Directory.Exists(_folders.Incoming));
        Assert.Equal(2, (await BookSources(client, ids[1])).Count);

        var rescan = await Scan(client);

        Assert.Equal(0, rescan.GetProperty("filesMissing").GetInt32());
    }

    [Fact]
    public async Task A_folder_left_holding_a_non_audio_file_stays()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(client);
        var cover = Path.Combine(_folders.Incoming, "alpha", "cover.jpg");
        File.WriteAllText(cover, "not audio");

        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        Assert.All(Sources(done), s => Assert.False(File.Exists(s)));
        Assert.Equal([cover], Directory.GetFileSystemEntries(Path.Combine(_folders.Incoming, "alpha")));
        Assert.Equal((0L, 0L), FileRows(FileIds(done)));
    }

    [Fact]
    public async Task With_keep_source_on_a_processed_books_rows_and_folder_stay()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true);
        var ids = await ScanTwoBooks(client);

        var done = await WaitForJob(client, (await ApproveJob(client, ids[0])).GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        Assert.Equal((2L, 2L), FileRows(FileIds(done)));
        Assert.All(Sources(done), s => Assert.True(File.Exists(s)));
        Assert.True(Directory.Exists(Path.Combine(_folders.Incoming, "alpha")));
    }

    [Fact]
    public async Task A_book_nested_two_folders_deep_removes_both_emptied_folders()
    {
        var client = _factory.CreateClient();
        await SaveTemplates(client, "%artist%/%album%", CleanFileTemplate);
        var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "author", "title")).FullName;
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track1.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        await Scan(client);
        var groupId = Assert.Single(await GroupIds(client));
        var renamed = await client.PutAsJsonAsync($"/api/groups/{groupId}", new { title = "Nested Book", author = "Ann Author" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var done = await WaitForJob(client, (await ApproveJob(client, groupId)).GetProperty("id").GetInt64());

        Assert.Equal("Succeeded", done.GetProperty("status").GetString());
        Assert.False(Directory.Exists(Path.Combine(_folders.Incoming, "author")));
        Assert.True(Directory.Exists(_folders.Incoming));
        Assert.Empty(Directory.GetFileSystemEntries(_folders.Incoming));
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

    /// <summary>Sets the job's status and each listed file's status (by position from 1) straight in the database.</summary>
    private static void SetStatuses(WebApplicationFactory<Program> host, long jobId, string jobStatus, params (int Position, string Status)[] files)
    {
        using var connection = host.Services.GetRequiredService<SqliteConnectionFactory>().Open();
        connection.Execute("UPDATE ProcessingJob SET Status = @jobStatus WHERE Id = @jobId", new { jobId, jobStatus });
        foreach (var (position, status) in files)
            connection.Execute("UPDATE ProcessingJobFile SET Status = @status WHERE JobId = @jobId AND Position = @position",
                new { jobId, position, status });
    }

    private static void PlaceFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static List<string> FileStatuses(JsonElement job) => job.GetProperty("files").EnumerateArray()
        .Select(f => f.GetProperty("status").GetString()!).ToList();

    [Fact]
    public async Task At_startup_an_interrupted_jobs_copies_and_partial_are_removed_and_the_book_can_run_again()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var jobId = created.GetProperty("id").GetInt64();
        var sources = Sources(created);
        var destinations = Destinations(created);
        var sourcesBefore = Hashes(sources);
        // A run cut off while writing file 2: file 1 is copied into place, file 2's temporary file is half-written.
        PlaceFile(sources[0], destinations[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(destinations[1])!);
        File.WriteAllBytes(destinations[1] + ".partial", [1, 2, 3]);
        SetStatuses(host, jobId, ProcessingJob.Running, (1, ProcessingJobFile.Copied));

        var client = _factory.CreateClient();
        var failed = await WaitForJob(client, jobId);

        Assert.Equal(("Failed", "Interrupted by an application restart."),
            (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.All(destinations, d => Assert.False(File.Exists(d)));
        Assert.Empty(PartialFiles());
        Assert.Equal(sourcesBefore, Hashes(sources));
        Assert.All(FileStatuses(failed), s => Assert.Equal("Pending", s));

        var retried = await ApproveJob(client, ids[0]);
        var ran = await WaitForJob(client, retried.GetProperty("id").GetInt64());
        Assert.Equal("Succeeded", ran.GetProperty("status").GetString());
    }

    /// <summary>
    /// A job cut off while deleting sources: both copies of the first book verified and placed, file 1's source already
    /// deleted, file 2 still Copied. <paramref name="beforeRestart"/> runs on the setup host just before the restart.
    /// </summary>
    private async Task<(long JobId, List<string> Sources, List<string> Destinations)> CutOffWhileDeleting(
        Func<HttpClient, List<string>, Task>? beforeRestart = null)
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var jobId = created.GetProperty("id").GetInt64();
        var sources = Sources(created);
        var destinations = Destinations(created);
        PlaceFile(sources[0], destinations[0]);
        PlaceFile(sources[1], destinations[1]);
        File.Delete(sources[0]);
        SetStatuses(host, jobId, ProcessingJob.Running, (1, ProcessingJobFile.SourceDeleted), (2, ProcessingJobFile.Copied));
        if (beforeRestart is not null) await beforeRestart(setup, sources);
        return (jobId, sources, destinations);
    }

    [Fact]
    public async Task At_startup_an_interrupted_job_with_a_deleted_source_finishes_deleting_and_succeeds_keeping_every_copy()
    {
        var (jobId, sources, destinations) = await CutOffWhileDeleting();
        var copiesBefore = Hashes(destinations);

        var done = await WaitForJob(_factory.CreateClient(), jobId);

        Assert.Equal(("Succeeded", JsonValueKind.Null), (done.GetProperty("status").GetString(), done.GetProperty("error").ValueKind));
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("finishedAtUtc").ValueKind);
        Assert.Equal(["SourceDeleted", "SourceDeleted"], FileStatuses(done));
        Assert.All(done.GetProperty("files").EnumerateArray(), f => Assert.Equal(JsonValueKind.Null, f.GetProperty("error").ValueKind));
        Assert.False(File.Exists(sources[1]));
        Assert.Equal(copiesBefore, Hashes(destinations));
        Assert.Equal((0L, 0L), FileRows(FileIds(done)));
        Assert.False(Directory.Exists(Path.Combine(_folders.Incoming, "alpha")));
        Assert.True(Directory.Exists(Path.Combine(_folders.Incoming, "beta")));
    }

    [Fact]
    public async Task At_startup_an_interrupted_jobs_changed_remaining_source_is_kept_with_an_error_and_the_job_succeeds()
    {
        var (jobId, sources, destinations) = await CutOffWhileDeleting((_, sources) =>
        {
            File.AppendAllText(sources[1], "changed");
            return Task.CompletedTask;
        });
        var copiesBefore = Hashes(destinations);
        var changedSource = Hashes([sources[1]]);

        var done = await WaitForJob(_factory.CreateClient(), jobId);

        Assert.Equal(("Succeeded", JsonValueKind.Null), (done.GetProperty("status").GetString(), done.GetProperty("error").ValueKind));
        var files = done.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal("SourceDeleted", files[0].GetProperty("status").GetString());
        Assert.Equal(("Copied", "The source changed after it was copied, so it was kept."),
            (files[1].GetProperty("status").GetString(), files[1].GetProperty("error").GetString()));
        Assert.Equal(changedSource, Hashes([sources[1]]));
        Assert.Equal(copiesBefore, Hashes(destinations));
        var ids = FileIds(done);
        Assert.Equal((0L, 0L), FileRows([ids[0]]));
        Assert.Equal((1L, 1L), FileRows([ids[1]]));
    }

    [Fact]
    public async Task At_startup_with_keep_source_on_an_interrupted_job_with_a_deleted_source_succeeds_and_keeps_the_rest()
    {
        var (jobId, sources, destinations) = await CutOffWhileDeleting((setup, _) =>
            SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate, keepSourceFiles: true));
        var copiesBefore = Hashes(destinations);
        var remainingSource = Hashes([sources[1]]);

        var done = await WaitForJob(_factory.CreateClient(), jobId);

        Assert.Equal(("Succeeded", JsonValueKind.Null), (done.GetProperty("status").GetString(), done.GetProperty("error").ValueKind));
        Assert.Equal(["SourceDeleted", "Copied"], FileStatuses(done));
        Assert.Equal(remainingSource, Hashes([sources[1]]));
        Assert.Equal(copiesBefore, Hashes(destinations));
    }

    [Fact]
    public async Task At_startup_an_interrupted_job_never_deletes_a_file_at_a_pending_destination()
    {
        using var host = QueueOnly();
        var setup = host.CreateClient();
        await SaveTemplates(setup, "%artist%/%album%", CleanFileTemplate);
        var ids = await ScanTwoBooks(setup);
        var created = await ApproveJob(setup, ids[0]);
        var jobId = created.GetProperty("id").GetInt64();
        var sources = Sources(created);
        var destinations = Destinations(created);
        var sourcesBefore = Hashes(sources);
        // Not this job's: the file appeared at file 1's destination after approval.
        Directory.CreateDirectory(Path.GetDirectoryName(destinations[0])!);
        File.WriteAllBytes(destinations[0], [4, 5, 6]);
        SetStatuses(host, jobId, ProcessingJob.Running);

        var failed = await WaitForJob(_factory.CreateClient(), jobId);

        Assert.Equal(("Failed", "Interrupted by an application restart."),
            (failed.GetProperty("status").GetString(), failed.GetProperty("error").GetString()));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(destinations[0]));
        Assert.Equal(sourcesBefore, Hashes(sources));
        Assert.All(FileStatuses(failed), s => Assert.Equal("Pending", s));
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

        var before = DateTime.UtcNow;

        host.Services.GetRequiredService<ProcessingService>().FailInterruptedJobs();

        (string, string?, string?) Read(long id)
        {
            var job = repository.GetJob(id)!.Value.Job;
            return (job.Status, job.Error, job.FinishedAtUtc);
        }
        var (status, error, finishedAt) = Read(running);
        Assert.Equal(("Failed", "Interrupted by an application restart."), (status, error));
        Assert.True(DateTime.Parse(finishedAt!, null, System.Globalization.DateTimeStyles.RoundtripKind) >= before);
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
