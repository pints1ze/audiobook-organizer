using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Tests.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class ReviewControllerTests : IDisposable
{
    // 1x1 PNG.
    private static readonly byte[] CoverPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    /// <summary>
    /// Replaces incoming's root files with the "alpha" and "beta" books (track1 and track2 MP3s each; alpha's track2
    /// with an embedded PNG when <paramref name="embeddedCover"/>), saves the templates and scans. Returns GET /api/groups.
    /// </summary>
    private async Task<List<JsonElement>> ScanTwoBooks(
        HttpClient client, string directoryTemplate = "%artist%/%album%", bool embeddedCover = false)
    {
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        foreach (var folder in new[] { "alpha", "beta" })
        {
            var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, folder)).FullName;
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track1.mp3"));
            File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        }
        if (embeddedCover)
        {
            using var file = TagLib.File.Create(Path.Combine(_folders.Incoming, "alpha", "track2.mp3"));
            file.Tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(CoverPng)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/png" },
            ];
            file.Save();
        }

        var settings = await client.PutAsJsonAsync("/api/settings", new
        {
            incomingPath = _folders.Incoming, libraryPath = _folders.Library, keepSourceFiles = false,
            directoryTemplate, fileTemplate = "[%track:2% - ]%album%",
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());

        var groups = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups")))
            .GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(2, groups.Count);
        return groups;
    }

    private static async Task<JsonElement> Review(HttpClient client)
    {
        var response = await client.GetAsync("/api/review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static JsonElement Book(JsonElement review, long groupId) =>
        review.GetProperty("books").EnumerateArray().Single(b => b.GetProperty("groupId").GetInt64() == groupId);

    private static int Count(JsonElement review, string stage) => review.GetProperty("counts").GetProperty(stage).GetInt32();

    private static async Task PickMatch(HttpClient client, long groupId)
    {
        var response = await client.PutAsJsonAsync($"/api/groups/{groupId}/match", new
        {
            provider = "audible",
            asin = "B0TEST0001",
            title = "The Test Book",
            authors = new[] { "Ann Author", "Second Author" },
            narrators = new[] { "Ned Narrator", "Other Narrator" },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task UploadCover(HttpClient client, long groupId)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(CoverPng);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(part, "file", "cover.png");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/groups/{groupId}/cover/upload", form)).StatusCode);
    }

    [Fact]
    public async Task Scanned_books_are_listed_as_NeedsMatch_in_groups_order_with_their_title_author_and_duration()
    {
        var client = _factory.CreateClient();
        var groups = await ScanTwoBooks(client);

        var review = await Review(client);

        var books = review.GetProperty("books").EnumerateArray().ToList();
        Assert.Equal(groups.Select(g => g.GetProperty("id").GetInt64()), books.Select(b => b.GetProperty("groupId").GetInt64()));
        foreach (var (group, book) in groups.Zip(books))
        {
            Assert.Equal("NeedsMatch", book.GetProperty("stage").GetString());
            Assert.Equal(group.GetProperty("title").GetString(), book.GetProperty("title").GetString());
            Assert.Equal(group.GetProperty("author").GetString(), book.GetProperty("author").GetString());
            Assert.Equal(JsonValueKind.Null, book.GetProperty("narrator").ValueKind);
            Assert.Equal(group.GetProperty("totalDurationMs").GetInt64(), book.GetProperty("durationMs").GetInt64());
            Assert.True(book.GetProperty("durationMs").GetInt64() > 0);
            Assert.Equal(2, book.GetProperty("fileCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, book.GetProperty("coverSource").ValueKind);
            Assert.Equal(JsonValueKind.Null, book.GetProperty("coverFileId").ValueKind);
        }
        Assert.Equal((2, 0, 0, 0, 0), (Count(review, "needsMatch"), Count(review, "needsReview"), Count(review, "ready"),
            Count(review, "queued"), Count(review, "failed")));
    }

    [Fact]
    public async Task A_matched_book_with_a_chosen_cover_and_a_clean_preview_is_Ready_with_the_match_values()
    {
        var client = _factory.CreateClient();
        var groups = await ScanTwoBooks(client);
        var id = groups[0].GetProperty("id").GetInt64();
        await PickMatch(client, id);
        await UploadCover(client, id);

        var review = await Review(client);

        var book = Book(review, id);
        Assert.Equal("Ready", book.GetProperty("stage").GetString());
        Assert.Equal("The Test Book", book.GetProperty("title").GetString());
        Assert.Equal("Ann Author", book.GetProperty("author").GetString());
        Assert.Equal("Ned Narrator", book.GetProperty("narrator").GetString());
        Assert.Equal("chosen", book.GetProperty("coverSource").GetString());
        Assert.Equal(JsonValueKind.Null, book.GetProperty("coverFileId").ValueKind);
        Assert.Equal((1, 1, 0), (Count(review, "needsMatch"), Count(review, "ready"), Count(review, "needsReview")));
    }

    [Fact]
    public async Task A_matched_book_whose_preview_has_a_missing_token_is_NeedsReview()
    {
        var client = _factory.CreateClient();
        var groups = await ScanTwoBooks(client, directoryTemplate: "%artist%/%series%/%album%");
        var id = groups[0].GetProperty("id").GetInt64();
        await PickMatch(client, id);
        await UploadCover(client, id);

        var review = await Review(client);

        var book = Book(review, id);
        Assert.Equal("NeedsReview", book.GetProperty("stage").GetString());
        Assert.Equal("chosen", book.GetProperty("coverSource").GetString());
        Assert.Equal((1, 0, 1), (Count(review, "needsMatch"), Count(review, "ready"), Count(review, "needsReview")));
    }

    [Fact]
    public async Task A_book_whose_job_succeeded_is_not_listed()
    {
        var client = _factory.CreateClient();
        var groups = await ScanTwoBooks(client);
        var processed = groups[0].GetProperty("id").GetInt64();
        var repository = _factory.Services.GetRequiredService<ProcessingRepository>();
        var jobId = repository.CreateJob(new ProcessingJob
        {
            GroupId = processed, Title = "Done", Status = ProcessingJob.Queued, CreatedAtUtc = "2026-10-25T00:00:00.0000000Z",
        }, []);
        var job = repository.GetJob(jobId)!.Value.Job;
        job.Status = ProcessingJob.Succeeded;
        repository.UpdateJob(job);

        var review = await Review(client);

        var ids = review.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("groupId").GetInt64()).ToList();
        Assert.Equal([groups[1].GetProperty("id").GetInt64()], ids);
        Assert.Equal(1, Count(review, "needsMatch"));
    }

    [Fact]
    public async Task A_book_with_an_embedded_cover_and_none_chosen_shows_the_first_file_with_one()
    {
        var client = _factory.CreateClient();
        await ScanTwoBooks(client, embeddedCover: true);
        var withCover = new Dictionary<long, long>();
        var groups = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"))).GetProperty("groups");
        foreach (var group in groups.EnumerateArray())
        {
            var detail = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{group.GetProperty("id").GetInt64()}"));
            foreach (var file in detail.GetProperty("files").EnumerateArray())
                if (file.GetProperty("relativePath").GetString() == "alpha/track2.mp3")
                    withCover[group.GetProperty("id").GetInt64()] = file.GetProperty("fileId").GetInt64();
        }
        var (alphaId, coverFileId) = withCover.Single();

        var review = await Review(client);

        var alpha = Book(review, alphaId);
        Assert.Equal("embedded", alpha.GetProperty("coverSource").GetString());
        Assert.Equal(coverFileId, alpha.GetProperty("coverFileId").GetInt64());
        var other = review.GetProperty("books").EnumerateArray().Single(b => b.GetProperty("groupId").GetInt64() != alphaId);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("coverSource").ValueKind);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
