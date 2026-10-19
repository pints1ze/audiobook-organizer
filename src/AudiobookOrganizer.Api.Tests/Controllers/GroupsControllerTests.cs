using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudiobookOrganizer.Api.Tests.Services;

namespace AudiobookOrganizer.Api.Tests.Controllers;

public class GroupsControllerTests : IDisposable
{
    private readonly AudiobookOrganizerWebApplicationFactory _factory = new();
    private readonly ScanTestFolders _folders = new();

    private static async Task<JsonElement> GetGroups(HttpClient client)
    {
        var response = await client.GetAsync("/api/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private async Task Scan(HttpClient client)
    {
        await _folders.SaveSettings(client);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/scans", null)).StatusCode);
        var done = await ScanTestFolders.WaitForFinish(client);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Fresh_database_returns_empty_lists_then_a_scan_returns_groups_and_unresolved_files()
    {
        var client = _factory.CreateClient();

        var empty = await GetGroups(client);
        Assert.Equal(0, empty.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, empty.GetProperty("unresolved").GetArrayLength());

        // Two untagged (no album) root MP3s with different names, plus the M4B fixture.
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(_folders.Incoming, "alpha.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_folders.Incoming, "beta.mp3"));
        await Scan(client);

        var result = await GetGroups(client);

        var group = Assert.Single(result.GetProperty("groups").EnumerateArray());
        Assert.True(group.GetProperty("id").GetInt64() > 0);
        Assert.Contains(group.GetProperty("confidence").GetString(), new[] { "High", "Medium" });
        Assert.Equal(1, group.GetProperty("fileCount").GetInt32());
        Assert.InRange(group.GetProperty("totalDurationMs").GetInt64(), 29_500, 30_500);
        Assert.True(group.GetProperty("evidence").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.String, group.GetProperty("evidence")[0].ValueKind);
        Assert.Equal("Fixture Author", group.GetProperty("author").GetString());
        Assert.Equal("chapters", group.GetProperty("title").GetString());

        var unresolved = result.GetProperty("unresolved").EnumerateArray().ToList();
        Assert.Equal(["alpha.mp3", "beta.mp3"],
            unresolved.Select(u => u.GetProperty("relativePath").GetString()).Order(StringComparer.Ordinal));
        Assert.All(unresolved, u =>
        {
            Assert.True(u.GetProperty("fileId").GetInt64() > 0);
            Assert.InRange(u.GetProperty("durationMs").GetInt64(), 29_500, 30_500);
        });
    }

    [Fact]
    public async Task Groups_needing_review_come_first()
    {
        var client = _factory.CreateClient();
        // Repeated track numbers in one folder with no disc info make a Low group; the lone M4B is Medium.
        var repeat = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "repeat")).FullName;
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(repeat, "01 a.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(repeat, "01 b.mp3"));
        await Scan(client);

        var groups = (await GetGroups(client)).GetProperty("groups").EnumerateArray().ToList();

        Assert.Equal(["Low", "Medium"], groups.Select(g => g.GetProperty("confidence").GetString()));
        Assert.Equal([2, 1], groups.Select(g => g.GetProperty("fileCount").GetInt32()));
    }

    [Fact]
    public async Task Put_trims_the_title_stores_a_blank_author_as_null_and_marks_the_group_corrected()
    {
        var client = _factory.CreateClient();
        await Scan(client);
        var id = (await GetGroups(client)).GetProperty("groups")[0].GetProperty("id").GetInt64();

        var response = await client.PutAsJsonAsync($"/api/groups/{id}", new { title = "  New Title  ", author = "   " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ScansControllerTests.ReadJson(response);
        Assert.Equal(id, updated.GetProperty("id").GetInt64());
        Assert.Equal("New Title", updated.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("author").ValueKind);
        Assert.True(updated.GetProperty("userCorrected").GetBoolean());

        var listed = (await GetGroups(client)).GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("id").GetInt64() == id);
        Assert.Equal("New Title", listed.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("author").ValueKind);
        Assert.True(listed.GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_id_and_400_for_a_too_long_title_without_changing_anything()
    {
        var client = _factory.CreateClient();
        await Scan(client);
        var before = (await GetGroups(client)).GetProperty("groups").GetRawText();
        var id = (await GetGroups(client)).GetProperty("groups")[0].GetProperty("id").GetInt64();

        var missing = await client.PutAsJsonAsync("/api/groups/999999", new { title = "x", author = "y" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var tooLong = await client.PutAsJsonAsync($"/api/groups/{id}", new { title = new string('a', 501), author = "y" });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var problem = await ScansControllerTests.ReadJson(tooLong);
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));

        Assert.Equal(before, (await GetGroups(client)).GetProperty("groups").GetRawText());
    }

    /// <summary>Scans with a two-file "book" folder plus the root M4B; returns the book's id and the M4B group's id.</summary>
    private async Task<(long BookId, long OtherId)> ScanTwoFileBook(HttpClient client)
    {
        var book = Directory.CreateDirectory(Path.Combine(_folders.Incoming, "book")).FullName;
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(book, "track1.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(book, "track2.mp3"));
        await Scan(client);

        var groups = (await GetGroups(client)).GetProperty("groups").EnumerateArray().ToList();
        return (groups.Single(g => g.GetProperty("fileCount").GetInt32() == 2).GetProperty("id").GetInt64(),
            groups.Single(g => g.GetProperty("fileCount").GetInt32() == 1).GetProperty("id").GetInt64());
    }

    private static async Task<JsonElement> GetDetail(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/groups/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ScansControllerTests.ReadJson(response);
    }

    private static List<string?> Paths(JsonElement detail) =>
        detail.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()).ToList();

    private static List<long> FileIds(JsonElement detail) =>
        detail.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("fileId").GetInt64()).ToList();

    [Fact]
    public async Task Get_by_id_returns_the_group_with_its_files_in_order_and_404_for_an_unknown_id()
    {
        var client = _factory.CreateClient();
        var (bookId, _) = await ScanTwoFileBook(client);

        var detail = await GetDetail(client, bookId);

        Assert.Equal(bookId, detail.GetProperty("id").GetInt64());
        Assert.Equal(2, detail.GetProperty("fileCount").GetInt32());
        Assert.False(detail.GetProperty("userCorrected").GetBoolean());
        Assert.Equal(["book/track1.mp3", "book/track2.mp3"], Paths(detail));
        var files = detail.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal([1, 2], files.Select(f => f.GetProperty("position").GetInt32()));
        Assert.All(files, f =>
        {
            Assert.True(f.GetProperty("fileId").GetInt64() > 0);
            Assert.InRange(f.GetProperty("durationMs").GetInt64(), 29_500, 30_500);
            Assert.True(f.TryGetProperty("disc", out _));
            Assert.True(f.TryGetProperty("track", out _));
        });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/groups/999999")).StatusCode);
    }

    [Fact]
    public async Task Put_order_reverses_the_files_and_marks_the_group_corrected()
    {
        var client = _factory.CreateClient();
        var (bookId, _) = await ScanTwoFileBook(client);
        var ids = FileIds(await GetDetail(client, bookId));
        ids.Reverse();

        var response = await client.PutAsJsonAsync($"/api/groups/{bookId}/order", new { fileIds = ids });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ScansControllerTests.ReadJson(response);
        Assert.True(updated.GetProperty("userCorrected").GetBoolean());
        Assert.Equal(["book/track2.mp3", "book/track1.mp3"], Paths(updated));
        Assert.Equal([1, 2], updated.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("position").GetInt32()));

        var reread = await GetDetail(client, bookId);
        Assert.Equal(ids, FileIds(reread));
        Assert.True(reread.GetProperty("userCorrected").GetBoolean());
    }

    [Fact]
    public async Task Put_order_rejects_a_bad_list_with_400_without_changing_anything_and_404s_an_unknown_group()
    {
        var client = _factory.CreateClient();
        var (bookId, otherId) = await ScanTwoFileBook(client);
        var before = (await GetDetail(client, bookId)).GetRawText();
        var ids = FileIds(await GetDetail(client, bookId));
        var otherFile = FileIds(await GetDetail(client, otherId)).Single();

        object[] bad =
        [
            new { fileIds = Array.Empty<long>() },
            new { },
            new { fileIds = new[] { ids[0], ids[0] } },
            new { fileIds = new[] { ids[0] } },
            new { fileIds = new[] { ids[0], otherFile } },
            new { fileIds = new[] { ids[0], ids[1], otherFile } },
        ];
        foreach (var body in bad)
        {
            var response = await client.PutAsJsonAsync($"/api/groups/{bookId}/order", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty("fileIds", out _), problem.GetRawText());
        }

        Assert.Equal(before, (await GetDetail(client, bookId)).GetRawText());

        var missing = await client.PutAsJsonAsync("/api/groups/999999/order", new { fileIds = ids });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>Scans two untagged root MP3s (unresolved) plus the M4B group; returns the unresolved ids and the group's id.</summary>
    private async Task<(List<long> Unresolved, long GroupId)> ScanTwoUnresolved(HttpClient client)
    {
        File.Move(Path.Combine(_folders.Incoming, "chapters.mp3"), Path.Combine(_folders.Incoming, "alpha.mp3"));
        File.Copy(ChapterReaderTests.Fixture("chapters.mp3"), Path.Combine(_folders.Incoming, "beta.mp3"));
        await Scan(client);
        var list = await GetGroups(client);
        return (
            list.GetProperty("unresolved").EnumerateArray()
                .OrderBy(u => u.GetProperty("relativePath").GetString(), StringComparer.Ordinal)
                .Select(u => u.GetProperty("fileId").GetInt64()).ToList(),
            list.GetProperty("groups")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Post_creates_a_group_from_unresolved_files_and_returns_201_with_a_location()
    {
        var client = _factory.CreateClient();
        var (unresolved, _) = await ScanTwoUnresolved(client);
        unresolved.Reverse();

        var response = await client.PostAsJsonAsync("/api/groups", new { fileIds = unresolved, title = " Two Parts ", author = "Me" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ScansControllerTests.ReadJson(response);
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal($"/api/groups/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("Two Parts", created.GetProperty("title").GetString());
        Assert.Equal("High", created.GetProperty("confidence").GetString());
        Assert.True(created.GetProperty("userCorrected").GetBoolean());
        Assert.Equal(["beta.mp3", "alpha.mp3"], Paths(created));
        Assert.Equal(created.GetRawText(), (await GetDetail(client, id)).GetRawText());

        var list = await GetGroups(client);
        Assert.Equal(0, list.GetProperty("unresolved").GetArrayLength());
        Assert.Equal(2, list.GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task Post_files_moves_an_unresolved_file_into_a_group()
    {
        var client = _factory.CreateClient();
        var (unresolved, groupId) = await ScanTwoUnresolved(client);

        var response = await client.PostAsJsonAsync($"/api/groups/{groupId}/files", new { fileIds = new[] { unresolved[0] } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await ScansControllerTests.ReadJson(response);
        Assert.Equal(groupId, moved.GetProperty("id").GetInt64());
        Assert.Equal(2, moved.GetProperty("fileCount").GetInt32());
        Assert.Equal(unresolved[0], FileIds(moved)[1]);
        Assert.True(moved.GetProperty("userCorrected").GetBoolean());
        var left = (await GetGroups(client)).GetProperty("unresolved").EnumerateArray().Select(u => u.GetProperty("fileId").GetInt64());
        Assert.Equal([unresolved[1]], left);
    }

    [Fact]
    public async Task Bad_move_and_create_requests_return_400_without_changing_anything_and_an_unknown_target_404s()
    {
        var client = _factory.CreateClient();
        var (unresolved, groupId) = await ScanTwoUnresolved(client);
        var inGroup = FileIds(await GetDetail(client, groupId)).Single();
        var before = (await GetGroups(client)).GetRawText();

        (string Url, object Body, string Field)[] bad =
        [
            ($"/api/groups/{groupId}/files", new { fileIds = Array.Empty<long>() }, "fileIds"),
            ($"/api/groups/{groupId}/files", new { }, "fileIds"),
            ($"/api/groups/{groupId}/files", new { fileIds = new[] { unresolved[0], unresolved[0] } }, "fileIds"),
            ($"/api/groups/{groupId}/files", new { fileIds = new[] { unresolved[0], 999_999L } }, "fileIds"),
            ($"/api/groups/{groupId}/files", new { fileIds = new[] { unresolved[0], inGroup } }, "fileIds"),
            ("/api/groups", new { fileIds = Array.Empty<long>(), title = "x" }, "fileIds"),
            ("/api/groups", new { fileIds = new[] { unresolved[0], unresolved[0] } }, "fileIds"),
            ("/api/groups", new { fileIds = new[] { 999_999L } }, "fileIds"),
            ("/api/groups", new { fileIds = unresolved, title = new string('a', 501) }, "title"),
            ("/api/groups", new { fileIds = unresolved, author = new string('a', 501) }, "author"),
        ];
        foreach (var (url, body, field) in bad)
        {
            var response = await client.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), problem.GetRawText());
        }
        Assert.Equal(before, (await GetGroups(client)).GetRawText());

        var missing = await client.PostAsJsonAsync("/api/groups/999999/files", new { fileIds = unresolved });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, (await GetGroups(client)).GetRawText());
    }

    [Fact]
    public async Task Post_excluded_returns_the_list_with_the_file_excluded()
    {
        var client = _factory.CreateClient();
        var (unresolved, _) = await ScanTwoUnresolved(client);

        var response = await client.PostAsJsonAsync("/api/groups/excluded", new { fileIds = new[] { unresolved[0] } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ScansControllerTests.ReadJson(response);
        var excluded = Assert.Single(list.GetProperty("excluded").EnumerateArray());
        Assert.Equal(unresolved[0], excluded.GetProperty("fileId").GetInt64());
        Assert.Equal("alpha.mp3", excluded.GetProperty("relativePath").GetString());
        Assert.InRange(excluded.GetProperty("durationMs").GetInt64(), 29_500, 30_500);
        Assert.Equal([unresolved[1]],
            list.GetProperty("unresolved").EnumerateArray().Select(u => u.GetProperty("fileId").GetInt64()));
        Assert.Equal(list.GetRawText(), (await GetGroups(client)).GetRawText());
    }

    [Fact]
    public async Task Bad_exclude_and_unresolve_requests_return_400_without_changing_anything()
    {
        var client = _factory.CreateClient();
        var (unresolved, groupId) = await ScanTwoUnresolved(client);
        var inGroup = FileIds(await GetDetail(client, groupId)).Single();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/groups/excluded", new { fileIds = new[] { unresolved[0] } })).StatusCode);
        var before = (await GetGroups(client)).GetRawText();

        (string Url, object Body)[] bad =
        [
            ("/api/groups/excluded", new { fileIds = Array.Empty<long>() }),
            ("/api/groups/excluded", new { }),
            ("/api/groups/excluded", new { fileIds = new[] { inGroup, inGroup } }),
            ("/api/groups/excluded", new { fileIds = new[] { inGroup, 999_999L } }),
            ("/api/groups/excluded", new { fileIds = new[] { inGroup, unresolved[0] } }),
            ("/api/groups/unresolved", new { fileIds = Array.Empty<long>() }),
            ("/api/groups/unresolved", new { fileIds = new[] { 999_999L } }),
            ("/api/groups/unresolved", new { fileIds = new[] { inGroup, unresolved[1] } }),
        ];
        foreach (var (url, body) in bad)
        {
            var response = await client.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ScansControllerTests.ReadJson(response);
            Assert.True(problem.GetProperty("errors").TryGetProperty("fileIds", out _), problem.GetRawText());
        }
        Assert.Equal(before, (await GetGroups(client)).GetRawText());
    }

    public void Dispose()
    {
        _factory.Dispose();
        _folders.Dispose();
    }
}
