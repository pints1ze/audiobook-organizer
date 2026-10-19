using System.Net;
using System.Text.Json;
using AudiobookOrganizer.Api.Models.OpenLibrary;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class OpenLibraryClientTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "openlibrary", name);

    private static FakeHttpMessageHandler Fixture(string name) => FakeHttpMessageHandler.Json(File.ReadAllText(FixturePath(name)));

    private static OpenLibraryClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") });

    [Theory]
    [InlineData("search-project-hail-mary.json")]
    [InlineData("search-isbn-9780593135204.json")]
    public void Fixtures_deserialize_and_find_at_least_one_book(string name)
    {
        var json = File.ReadAllText(FixturePath(name));

        var response = JsonSerializer.Deserialize<OpenLibrarySearchResponse>(json);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Docs);
        using var raw = JsonDocument.Parse(json);
        Assert.True(raw.RootElement.GetProperty("numFound").GetInt32() >= 1);
    }

    [Fact]
    public async Task Project_hail_mary_maps_to_a_print_candidate()
    {
        var handler = Fixture("search-project-hail-mary.json");

        var candidates = await Client(handler).SearchAsync("project hail mary", CancellationToken.None);

        var book = candidates[0];
        Assert.Equal("openlibrary", book.Provider);
        Assert.Equal("Project Hail Mary", book.Title);
        Assert.Equal(["Andy Weir"], book.Authors);
        Assert.Equal("2021", book.ReleaseDate);
        Assert.Equal("https://covers.openlibrary.org/b/id/11200092-L.jpg", book.CoverUrl);
        Assert.Null(book.Asin);
        Assert.Null(book.Isbn);
        Assert.Empty(book.Narrators);
        Assert.Null(book.Publisher);
        Assert.Null(book.Language);
        Assert.Null(book.RuntimeMinutes);
        Assert.Null(book.Format);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("https://openlibrary.org/search.json", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("q=project%20hail%20mary", uri.Query);
        Assert.Contains("limit=20", uri.Query);
        Assert.Contains("fields=key,title,subtitle,author_name,first_publish_year,cover_i", uri.Query);
    }

    [Fact]
    public async Task Book_without_a_cover_gets_no_cover_url()
    {
        var candidates = await Client(Fixture("search-project-hail-mary.json"))
            .SearchAsync("project hail mary", CancellationToken.None);

        Assert.Null(candidates[1].CoverUrl);
    }

    [Fact]
    public async Task Isbn_query_searches_by_isbn_and_sets_it_on_the_candidate()
    {
        var handler = Fixture("search-isbn-9780593135204.json");

        var candidates = await Client(handler).SearchAsync("978-0-593-13520-4", CancellationToken.None);

        var book = Assert.Single(candidates);
        Assert.Equal("9780593135204", book.Isbn);
        Assert.Equal("Project Hail Mary", book.Title);
        var query = Assert.Single(handler.Requests).RequestUri!.Query;
        Assert.Contains("isbn=9780593135204", query);
        Assert.DoesNotContain("q=", query);
    }

    [Fact]
    public async Task Service_unavailable_throws_provider_exception()
    {
        var client = Client(FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<MetadataProviderException>(() => client.SearchAsync("x", CancellationToken.None));
        Assert.StartsWith("Open Library search failed", ex.Message);
    }

    [Fact]
    public async Task Malformed_body_throws_provider_exception()
    {
        var client = Client(FakeHttpMessageHandler.Json("<html>not json"));

        var ex = await Assert.ThrowsAsync<MetadataProviderException>(() => client.SearchAsync("x", CancellationToken.None));
        Assert.StartsWith("Open Library search failed", ex.Message);
    }

    [Fact]
    public async Task Timeout_throws_provider_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        await Assert.ThrowsAsync<MetadataProviderException>(() => Client(handler).SearchAsync("x", CancellationToken.None));
    }
}
