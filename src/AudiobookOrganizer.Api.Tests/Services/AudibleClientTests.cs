using System.Net;
using System.Text.Json;
using AudiobookOrganizer.Api.Models.Audible;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class AudibleClientTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "audible", name);

    private static AudibleClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.audible.com/") });

    [Theory]
    [InlineData("search-project-hail-mary.json")]
    [InlineData("search-dungeon-crawler-carl.json")]
    public void Fixtures_deserialize_into_the_external_shape(string name)
    {
        var response = JsonSerializer.Deserialize<AudibleCatalogResponse>(File.ReadAllText(FixturePath(name)));

        Assert.NotNull(response);
        Assert.NotEmpty(response.Products);
        Assert.All(response.Products, p => Assert.False(string.IsNullOrWhiteSpace(p.Asin)));
    }

    [Fact]
    public void Dungeon_crawler_carl_fixture_has_a_series()
    {
        var response = JsonSerializer.Deserialize<AudibleCatalogResponse>(
            File.ReadAllText(FixturePath("search-dungeon-crawler-carl.json")))!;

        Assert.NotNull(response.Products[0].Series);
        Assert.NotEmpty(response.Products[0].Series!);
    }

    [Fact]
    public async Task Project_hail_mary_maps_to_a_standalone_candidate()
    {
        var candidates = await Client(FakeHttpMessageHandler.Fixture("search-project-hail-mary.json"))
            .SearchAsync("project hail mary", CancellationToken.None);

        var book = candidates[0];
        Assert.Equal("audible", book.Provider);
        Assert.Equal("B08G9PRS1K", book.Asin);
        Assert.Equal("Project Hail Mary", book.Title);
        Assert.Null(book.Subtitle);
        Assert.Equal(["Andy Weir"], book.Authors);
        Assert.Equal(["Ray Porter"], book.Narrators);
        Assert.Null(book.Series);
        Assert.Equal("Audible Studios", book.Publisher);
        Assert.Equal("2021-05-04", book.ReleaseDate);
        Assert.Equal("english", book.Language);
        Assert.Equal(970, book.RuntimeMinutes);
        Assert.Equal("unabridged", book.Format);
        Assert.Equal("https://m.media-amazon.com/images/I/51POf8gOyLL._SL500_.jpg", book.CoverUrl);
    }

    [Fact]
    public async Task Dungeon_crawler_carl_maps_series_name_and_part_from_the_first_series()
    {
        var candidates = await Client(FakeHttpMessageHandler.Fixture("search-dungeon-crawler-carl.json"))
            .SearchAsync("dungeon crawler carl", CancellationToken.None);

        var book = candidates[0];
        Assert.Equal("B0934GTSGT", book.Asin);
        Assert.Equal("Dungeon Crawler Carl, Book 2", book.Subtitle);
        Assert.Equal(new("Dungeon Crawler Carl", "2"), book.Series);
    }

    [Fact]
    public async Task Request_carries_the_response_groups_and_encoded_keywords()
    {
        var handler = FakeHttpMessageHandler.Fixture("search-project-hail-mary.json");

        await Client(handler).SearchAsync("hail mary & co", CancellationToken.None);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal("https://api.audible.com/1.0/catalog/products", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains(
            "response_groups=contributors,media,product_desc,product_attrs,product_extended_attrs,series",
            uri.Query);
        Assert.Contains("image_sizes=500", uri.Query);
        Assert.Contains("products_sort_by=Relevance", uri.Query);
        Assert.Contains("keywords=hail%20mary%20%26%20co", uri.Query);
    }

    [Fact]
    public async Task Empty_contributor_names_are_dropped_and_strings_trimmed()
    {
        const string body = """
            {"products":[{"asin":" X1 ","title":" T ","authors":[{"name":" "},{"name":" A "}],"narrators":[{"name":""}],
              "product_images":{"250":"small.jpg","1000":"big.jpg"}}]}
            """;

        var book = Assert.Single(await Client(FakeHttpMessageHandler.Json(body)).SearchAsync("x", CancellationToken.None));

        Assert.Equal("X1", book.Asin);
        Assert.Equal("T", book.Title);
        Assert.Equal(["A"], book.Authors);
        Assert.Empty(book.Narrators);
        Assert.Equal("big.jpg", book.CoverUrl);
    }

    [Fact]
    public async Task Service_unavailable_throws_provider_exception()
    {
        var client = Client(FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<MetadataProviderException>(() => client.SearchAsync("x", CancellationToken.None));
        Assert.Contains("Audible", ex.Message);
    }

    [Fact]
    public async Task Malformed_body_throws_provider_exception()
    {
        var client = Client(FakeHttpMessageHandler.Json("<html>not json"));

        await Assert.ThrowsAsync<MetadataProviderException>(() => client.SearchAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_throws_provider_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        await Assert.ThrowsAsync<MetadataProviderException>(() => Client(handler).SearchAsync("x", CancellationToken.None));
    }
}
