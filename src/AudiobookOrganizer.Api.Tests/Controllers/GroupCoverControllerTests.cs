using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Tests.Services;
using Dapper;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupCoverControllerTests : IDisposable
{
    // 1x1 PNG.
    private static readonly byte[] CoverPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    // 8x8 grey JPEG generated with ffmpeg (as in TagRoundTripTests).
    private static readonly byte[] CoverJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAPTGF2YzYzLjEuMTAyAP/bAEMACBQUFxQXGxsbGxsbIB4gISEhICAgICEhISQkJCoqKiQkJCEhJCQoKCoqLi8uKysqKy8vMjIyPDw5OUZGSFZWZ//EAEoAAQAAAAAAAAAAAAAAAAAAAAABAQAAAAAAAAAAAAAAAAAAAAAQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAAIAAgDASIAAhEAAxEA/9oADAMBAAIRAxEAPwAAD//Z");

    private const string AudibleCoverUrl = "https://m.media-amazon.com/images/I/test.jpg";

    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    // What the fake image CDN answers; every test's downloads go through it, so none reaches the network.
    private Func<HttpRequestMessage, HttpResponseMessage> _cdn = _ => Image(CoverJpeg, "image/jpeg");
    private readonly FakeHttpMessageHandler _cdnHandler;

    public GroupCoverControllerTests() => _cdnHandler = new FakeHttpMessageHandler(request => _cdn(request));

    private HttpClient Client() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddHttpClient<CoverDownloadClient>().ConfigurePrimaryHttpMessageHandler(() => _cdnHandler)))
            .CreateClient();

    private static HttpResponseMessage Image(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>Picks an Audible candidate with <paramref name="coverUrl"/> as the group's match.</summary>
    private static async Task PickMatch(HttpClient client, long groupId, string? coverUrl, string asin = "B0TEST0001")
    {
        var response = await client.PutAsJsonAsync($"/api/groups/{groupId}/match", new
        {
            provider = "audible",
            asin,
            title = "The Test Book",
            authors = new[] { "Ann Author" },
            narrators = new[] { "Ned Narrator" },
            coverUrl,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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
        var client = Client();
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
        var client = Client();
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
    public async Task Get_file_cover_serves_only_jpeg_or_png_whatever_type_the_tag_claims()
    {
        var client = Client();
        var books = await ScanBooks(client, odd: true);
        var (htmlId, jpgId) = (books.FileIds["odd/gif.mp3"], books.FileIds["odd/huge.mp3"]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{books.BookId}/files",
            new { fileIds = new[] { htmlId, jpgId } })).StatusCode);
        // A tag can name any MIME type; one claiming text/html must never be served as a page.
        var dir = Path.Combine(_folders.Incoming, "odd");
        SetPicture(Path.Combine(dir, "gif.mp3"), "<script>alert(1)</script>"u8.ToArray(), "text/html");
        SetPicture(Path.Combine(dir, "huge.mp3"), CoverJpeg, "image/jpg");

        var html = await client.GetAsync($"/api/groups/{books.BookId}/files/{htmlId}/cover");
        var jpg = await client.GetAsync($"/api/groups/{books.BookId}/files/{jpgId}/cover");

        Assert.Equal(HttpStatusCode.NotFound, html.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jpg.StatusCode);
        Assert.Equal("image/jpeg", jpg.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverJpeg, await jpg.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Put_cover_stores_the_embedded_picture_marks_the_group_corrected_and_a_second_pick_replaces_it()
    {
        var client = Client();
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
        var client = Client();
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

    [Fact]
    public async Task Get_cover_offers_the_matchs_cover_and_put_provider_downloads_and_stores_it()
    {
        var client = Client();
        var books = await ScanBooks(client);
        Assert.Equal(JsonValueKind.Null, (await GetCover(client, books.BookId)).GetProperty("provider").ValueKind);
        await PickMatch(client, books.BookId, AudibleCoverUrl);

        var offered = (await GetCover(client, books.BookId)).GetProperty("provider");
        Assert.Equal("audible", offered.GetProperty("provider").GetString());
        Assert.Equal(AudibleCoverUrl, offered.GetProperty("coverUrl").GetString());
        Assert.Single((await GetCover(client, books.BookId)).GetProperty("embedded").EnumerateArray());
        Assert.Empty(_cdnHandler.Requests);

        // The CDN's content type is ignored: the bytes say JPEG. A fileId in the body is ignored too.
        _cdn = _ => Image(CoverJpeg, "binary/octet-stream");
        var put = await Choose(client, books.BookId, new { source = "provider", fileId = books.PngFileId });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ScansControllerTests.ReadJson(put);
        var chosen = body.GetProperty("chosen");
        Assert.Equal("provider", chosen.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, chosen.GetProperty("fileId").ValueKind);
        Assert.Equal("image/jpeg", chosen.GetProperty("mimeType").GetString());
        Assert.Equal(body.GetRawText(), (await GetCover(client, books.BookId)).GetRawText());
        var image = await client.GetAsync($"/api/groups/{books.BookId}/cover/image");
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverJpeg, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(new Uri(AudibleCoverUrl), Assert.Single(_cdnHandler.Requests).RequestUri);
        Assert.Equal(1, CoverCount());

        // A PNG from the CDN replaces it, whatever the header says.
        _cdn = _ => Image(CoverPng, "image/jpeg");
        Assert.Equal(HttpStatusCode.OK, (await Choose(client, books.BookId, new { source = "provider" })).StatusCode);
        Assert.Equal("image/png", (await GetCover(client, books.BookId)).GetProperty("chosen").GetProperty("mimeType").GetString());
        Assert.Equal(1, CoverCount());
    }

    [Fact]
    public async Task Provider_is_chosen_only_while_the_chosen_cover_came_from_the_current_match()
    {
        var client = Client();
        var books = await ScanBooks(client);
        await PickMatch(client, books.BookId, AudibleCoverUrl);
        Assert.False((await GetCover(client, books.BookId)).GetProperty("provider").GetProperty("isChosen").GetBoolean());

        var put = await Choose(client, books.BookId, new { source = "provider" });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.True((await ScansControllerTests.ReadJson(put)).GetProperty("provider").GetProperty("isChosen").GetBoolean());
        Assert.True((await GetCover(client, books.BookId)).GetProperty("provider").GetProperty("isChosen").GetBoolean());

        // A different match: the stored cover still came from the old one.
        await PickMatch(client, books.BookId, "https://m.media-amazon.com/images/I/other.jpg", asin: "B0TEST0002");
        var afterPick = await GetCover(client, books.BookId);
        Assert.Equal("provider", afterPick.GetProperty("chosen").GetProperty("source").GetString());
        Assert.False(afterPick.GetProperty("provider").GetProperty("isChosen").GetBoolean());

        // Choosing it again downloads the new match's cover and marks it chosen.
        Assert.Equal(HttpStatusCode.OK, (await Choose(client, books.BookId, new { source = "provider" })).StatusCode);
        Assert.True((await GetCover(client, books.BookId)).GetProperty("provider").GetProperty("isChosen").GetBoolean());

        // An embedded cover isn't the match's.
        var embedded = await Choose(client, books.BookId, new { source = "embedded", fileId = books.PngFileId });
        Assert.Equal(HttpStatusCode.OK, embedded.StatusCode);
        Assert.False((await ScansControllerTests.ReadJson(embedded)).GetProperty("provider").GetProperty("isChosen").GetBoolean());
        Assert.False((await GetCover(client, books.BookId)).GetProperty("provider").GetProperty("isChosen").GetBoolean());
    }

    [Fact]
    public async Task Put_provider_marks_the_group_corrected()
    {
        var client = Client();
        var books = await ScanBooks(client);
        await PickMatch(client, books.OtherId, AudibleCoverUrl);
        // Picking the match already corrected the group; reset it so only the cover choice can set it.
        using (var connection = new SqliteConnectionFactory(_factory.DatabasePath).Open())
            connection.Execute("UPDATE FileGroup SET UserCorrected = 0 WHERE Id = @id", new { id = books.OtherId });

        Assert.Equal(HttpStatusCode.OK, (await Choose(client, books.OtherId, new { source = "provider" })).StatusCode);

        Assert.True((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{books.OtherId}")))
            .GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task Put_provider_returns_400_for_no_cover_url_an_unknown_host_or_bad_bytes_storing_nothing()
    {
        var client = Client();
        var books = await ScanBooks(client);
        const string noCover = "Choose a match with a cover first.";
        const string unknownHost = "The cover URL isn't from a known catalog.";

        async Task ExpectError(long groupId, string message)
        {
            var response = await Choose(client, groupId, new { source = "provider" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty("source", out var messages), problem.GetRawText());
            Assert.Equal(message, messages[0].GetString());
        }

        await ExpectError(books.BookId, noCover);
        await PickMatch(client, books.BookId, null);
        await ExpectError(books.BookId, noCover);
        Assert.Equal(JsonValueKind.Null, (await GetCover(client, books.BookId)).GetProperty("provider").ValueKind);
        foreach (var url in new[]
                 {
                     "https://192.168.1.10/x.jpg", "http://m.media-amazon.com/images/I/test.jpg",
                     "https://m.media-amazon.com.evil.example/x.jpg", "https://localhost/x.jpg",
                 })
        {
            await PickMatch(client, books.BookId, url);
            await ExpectError(books.BookId, unknownHost);
        }
        Assert.Empty(_cdnHandler.Requests);

        await PickMatch(client, books.BookId, "https://covers.openlibrary.org/b/id/1-L.jpg");
        _cdn = _ => Image("<html><script>alert(1)</script></html>"u8.ToArray(), "image/jpeg");
        await ExpectError(books.BookId, "The cover must be a JPEG or PNG.");
        var huge = new byte[CoverService.MaxCoverBytes + 1];
        CoverJpeg.CopyTo(huge, 0);
        _cdn = _ => Image(huge, "image/jpeg");
        await ExpectError(books.BookId, "The cover can be at most 10 MB.");

        Assert.Equal(0, CoverCount());
    }

    [Fact]
    public async Task Put_provider_returns_502_when_the_download_fails_and_the_embedded_source_still_works()
    {
        var client = Client();
        var books = await ScanBooks(client);
        await PickMatch(client, books.BookId, AudibleCoverUrl);
        _cdn = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var response = await Choose(client, books.BookId, new { source = "provider" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("The cover couldn't be downloaded. Try again later.",
            (await ScansControllerTests.ReadJson(response)).GetProperty("detail").GetString());
        Assert.Equal(0, CoverCount());

        Assert.Equal(HttpStatusCode.OK,
            (await Choose(client, books.BookId, new { source = "embedded", fileId = books.PngFileId })).StatusCode);
        Assert.Equal(1, CoverCount());
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, long groupId, byte[]? image, string fileName = "cover.jpg",
        string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        if (image is null)
        {
            form.Add(new StringContent("nothing"), "other");
        }
        else
        {
            var part = new ByteArrayContent(image);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(part, "file", fileName);
        }
        return client.PutAsync($"/api/groups/{groupId}/cover/upload", form);
    }

    [Fact]
    public async Task Put_upload_stores_the_image_typed_by_its_bytes_marks_the_group_corrected_and_a_second_upload_replaces_it()
    {
        var client = Client();
        var books = await ScanBooks(client);

        // A PNG sent as cover.jpg with image/jpeg: the bytes decide.
        var put = await Upload(client, books.OtherId, CoverPng);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ScansControllerTests.ReadJson(put);
        var chosen = body.GetProperty("chosen");
        Assert.Equal("upload", chosen.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, chosen.GetProperty("fileId").ValueKind);
        Assert.Equal("image/png", chosen.GetProperty("mimeType").GetString());
        Assert.Equal(body.GetRawText(), (await GetCover(client, books.OtherId)).GetRawText());
        var image = await client.GetAsync($"/api/groups/{books.OtherId}/cover/image");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CoverPng, await image.Content.ReadAsByteArrayAsync());
        Assert.True((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{books.OtherId}")))
            .GetProperty("userCorrected").GetBoolean());

        var second = await Upload(client, books.OtherId, CoverJpeg, "cover.png", "image/png");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("image/jpeg", (await GetCover(client, books.OtherId)).GetProperty("chosen").GetProperty("mimeType").GetString());
        image = await client.GetAsync($"/api/groups/{books.OtherId}/cover/image");
        Assert.Equal(CoverJpeg, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, CoverCount());
    }

    [Fact]
    public async Task Put_upload_returns_400_on_file_for_a_missing_empty_non_image_or_too_large_file_and_404_storing_nothing()
    {
        var client = Client();
        var books = await ScanBooks(client);
        var huge = new byte[CoverService.MaxCoverBytes + 1];
        CoverPng.CopyTo(huge, 0);

        (byte[]? Image, string FileName, string Message)[] bad =
        [
            (null, "cover.jpg", "Choose an image to upload."),
            ([], "cover.jpg", "Choose an image to upload."),
            ("<html><script>alert(1)</script></html>"u8.ToArray(), "cover.png", "The cover must be a JPEG or PNG."),
            (huge, "cover.png", "The cover can be at most 10 MB."),
        ];
        foreach (var (image, fileName, message) in bad)
        {
            var response = await Upload(client, books.BookId, image, fileName, "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty("file", out var messages), problem.GetRawText());
            Assert.Equal(message, messages[0].GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(client, 999999, CoverPng)).StatusCode);

        Assert.Equal(0, CoverCount());
        Assert.False((await ScansControllerTests.ReadJson(await client.GetAsync($"/api/groups/{books.BookId}")))
            .GetProperty("userCorrected").GetBoolean());
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
