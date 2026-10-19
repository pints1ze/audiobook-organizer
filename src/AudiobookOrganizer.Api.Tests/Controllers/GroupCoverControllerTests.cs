using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupCoverControllerTests : IDisposable
{
    // 1x1 PNG.
    private static readonly byte[] CoverPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // 8x8 grey JPEG generated with ffmpeg (as in TagRoundTripTests).
    private static readonly byte[] CoverJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAPTGF2YzYzLjEuMTAyAP/bAEMACBQUFxQXGxsbGxsbIB4gISEhICAgICEhISQkJCoqKiQkJCEhJCQoKCoqLi8uKysqKy8vMjIyPDw5OUZGSFZWZ//EAEoAAQAAAAAAAAAAAAAAAAAAAAABAQAAAAAAAAAAAAAAAAAAAAAQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAAIAAgDASIAAhEAAxEA/9oADAMBAAIRAxEAPwAAD//Z");

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    /// <summary>
    /// The scanned books: "book" (track1 with a PNG, track2 without a cover) and "other" (track1 with a JPEG), plus with
    /// <c>odd</c> an "odd" book (gif.mp3 with a GIF, huge.mp3 with a PNG over 10 MB).
    /// </summary>
    private record Books(long BookId, long PngFileId, long BareFileId, long OtherId, long JpegFileId,
        IReadOnlyDictionary<string, long> FileIds);

    /// <summary>Copies the MP3 fixture into book folders, adds covers to the temp copies with TagLibSharp, and scans.</summary>
    private async Task<Books> ScanBooks(HttpClient client, bool odd = false)
    {
        File.Delete(Path.Combine(_folders.Incoming, "chapters.m4b"));
        File.Delete(Path.Combine(_folders.Incoming, "chapters.mp3"));
        CopyMp3("book", "track1.mp3", CoverPng, "image/png");
        CopyMp3("book", "track2.mp3", null, null);
        CopyMp3("other", "track1.mp3", CoverJpeg, "image/jpeg");
        if (odd)
        {
            CopyMp3("odd", "gif.mp3", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "image/gif");
            CopyMp3("odd", "huge.mp3", new byte[10 * 1024 * 1024 + 1], "image/png");
        }

        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        Assert.Equal("Completed", (await ScanTestFolders.WaitForFinish(client)).GetProperty("status").GetString());

        // Relative path -> (group id, file id) across all groups.
        var files = new Dictionary<string, (long GroupId, long FileId)>();
        var groups = (await ScansControllerTests.ReadJson(await client.GetAsync("/api/groups"))).GetProperty("groups");
        foreach (var group in groups.EnumerateArray())
        {
            var groupId = group.GetProperty("id").GetInt64();
            var detail = await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{groupId}"));
            foreach (var file in detail.GetProperty("files").EnumerateArray())
                files[file.GetProperty("relativePath").GetString()!] = (groupId, file.GetProperty("fileId").GetInt64());
        }
        Assert.Equal(files["book/track1.mp3"].GroupId, files["book/track2.mp3"].GroupId);
        return new Books(files["book/track1.mp3"].GroupId, files["book/track1.mp3"].FileId, files["book/track2.mp3"].FileId,
            files["other/track1.mp3"].GroupId, files["other/track1.mp3"].FileId,
            files.ToDictionary(f => f.Key, f => f.Value.FileId));
    }

    private void CopyMp3(string folder, string name, byte[]? picture, string? mimeType)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_folders.Incoming, folder)).FullName;
        var path = Path.Combine(dir, name);
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), path);
        if (picture is not null) SetPicture(path, picture, mimeType!);
    }

    private static void SetPicture(string path, byte[] picture, string mimeType)
    {
        using var file = TagLib.File.Create(path);
        file.Tag.Pictures =
        [
            new TagLib.Picture(new TagLib.ByteVector(picture)) { Type = TagLib.PictureType.FrontCover, MimeType = mimeType },
        ];
        file.Save();
    }

    private static async Task<Dictionary<string, long>> FileIds(HttpClient client, long groupId) =>
        (await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{groupId}")))
        .GetProperty("files").EnumerateArray()
        .ToDictionary(f => f.GetProperty("relativePath").GetString()!, f => f.GetProperty("fileId").GetInt64());

    private static async Task<JsonElement> GetCover(HttpClient client, long groupId)
    {
        var response = await client.GetAsync($"/api/groups/{groupId}/cover");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static Task<HttpResponseMessage> Choose(HttpClient client, long groupId, object body) =>
        client.PutAsJsonAsync($"/api/groups/{groupId}/cover", body);

    private long CoverCount()
    {
        using var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open();
        return connection.ExecuteScalar<long>("SELECT COUNT(*) FROM AudiobookCover");
    }

    [Fact]
    public async Task Get_cover_lists_only_the_files_with_an_embedded_cover_and_no_choice_and_404s_an_unknown_group()
    {
        var client = _factory.CreateClient();
        var books = await ScanBooks(client);

        var cover = await GetCover(client, books.BookId);

        Assert.Equal(books.BookId, cover.GetProperty("groupId").GetInt64());
        Assert.Equal(JsonValueKind.Null, cover.GetProperty("chosen").ValueKind);
        var embedded = Assert.Single(cover.GetProperty("embedded").EnumerateArray());
        Assert.Equal(books.PngFileId, embedded.GetProperty("fileId").GetInt64());
        Assert.Equal("book/track1.mp3", embedded.GetProperty("relativePath").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/cover")).StatusCode);
    }

    [Fact]
    public async Task Get_file_cover_returns_the_exact_picture_bytes_and_never_changes_the_source_file()
    {
        var client = _factory.CreateClient();
        var books = await ScanBooks(client);
        var source = Path.Combine(_folders.Incoming, "book", "track1.mp3");
        var bytesBefore = await File.ReadAllBytesAsync(source);
        var mtimeBefore = File.GetLastWriteTimeUtc(source);

        var response = await client.GetAsync($"/api/groups/{books.BookId}/files/{books.PngFileId}/cover");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverPng, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(source));
        Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(source));

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/groups/{books.BookId}/files/{books.BareFileId}/cover")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/groups/{books.BookId}/files/{books.JpegFileId}/cover")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/groups/999999/files/{books.PngFileId}/cover")).StatusCode);
    }

    [Fact]
    public async Task Put_cover_stores_the_embedded_picture_marks_the_group_corrected_and_a_second_pick_replaces_it()
    {
        var client = _factory.CreateClient();
        var books = await ScanBooks(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{books.BookId}/cover/image")).StatusCode);

        var put = await Choose(client, books.BookId, new { source = "embedded", fileId = books.PngFileId });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ScansControllerTests.ReadJson(put);
        var chosen = body.GetProperty("chosen");
        Assert.Equal("embedded", chosen.GetProperty("source").GetString());
        Assert.Equal(books.PngFileId, chosen.GetProperty("fileId").GetInt64());
        Assert.Equal("image/png", chosen.GetProperty("mimeType").GetString());
        Assert.True(chosen.GetProperty("chosenAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(body.GetRawText(), (await GetCover(client, books.BookId)).GetRawText());

        var image = await client.GetAsync($"/api/groups/{books.BookId}/cover/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverPng, await image.Content.ReadAsByteArrayAsync());
        Assert.True((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{books.BookId}")))
            .GetProperty("userCorrected").GetBoolean());

        // The other book's JPEG can't be picked for this book, but replaces the PNG once the file is moved in.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{books.BookId}/files",
            new { fileIds = new[] { books.JpegFileId } })).StatusCode);
        var second = await Choose(client, books.BookId, new { source = "embedded", fileId = books.JpegFileId });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var replaced = (await GetCover(client, books.BookId)).GetProperty("chosen");
        Assert.Equal(books.JpegFileId, replaced.GetProperty("fileId").GetInt64());
        Assert.Equal("image/jpeg", replaced.GetProperty("mimeType").GetString());
        image = await client.GetAsync($"/api/groups/{books.BookId}/cover/image");
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverJpeg, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, CoverCount());
    }

    [Fact]
    public async Task Put_cover_returns_400_with_the_field_for_bad_choices_and_404_for_an_unknown_group_storing_nothing()
    {
        var client = _factory.CreateClient();
        var books = await ScanBooks(client, odd: true);
        // Move the GIF and the over-10-MB files into the book, so only their pictures make them invalid.
        var (gifId, hugeId) = (books.FileIds["odd/gif.mp3"], books.FileIds["odd/huge.mp3"]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{books.BookId}/files",
            new { fileIds = new[] { gifId, hugeId } })).StatusCode);

        (object Body, string Field, string Message)[] bad =
        [
            (new { source = "embedded", fileId = books.BareFileId }, "fileId", "This file has no embedded cover."),
            (new { source = "embedded", fileId = books.JpegFileId }, "fileId", "This file isn't in the book."),
            (new { source = "embedded", fileId = 999999 }, "fileId", "This file isn't in the book."),
            (new { source = "embedded" }, "fileId", "This file isn't in the book."),
            (new { source = "embedded", fileId = gifId }, "fileId", "The cover must be a JPEG or PNG."),
            (new { source = "embedded", fileId = hugeId }, "fileId", "The cover can be at most 10 MB."),
            (new { source = "upload", fileId = books.PngFileId }, "source", "Choose a cover source."),
            (new { fileId = books.PngFileId }, "source", "Choose a cover source."),
        ];
        foreach (var (body, field, message) in bad)
        {
            var response = await Choose(client, books.BookId, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out var messages), problem.GetRawText());
            Assert.Equal(message, messages[0].GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await Choose(client, 999999, new { source = "embedded", fileId = books.PngFileId })).StatusCode);

        Assert.Equal(0, CoverCount());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/groups/{books.BookId}/cover/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999/cover/image")).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
