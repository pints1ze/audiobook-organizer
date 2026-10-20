using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Tests.Services;

public class BookTokensTests
{
    private static MetadataFieldDto<string?> Text(string? value) => new(value, value is null ? null : "user", null);
    private static MetadataFieldDto<IReadOnlyList<string>> List(params string[] values) => new(values, "user", null);

    private static BookMetadataFieldsDto Fields(string? releaseDate = "2021-05-04") => new(
        Text("The Way of Kings"), Text("Book One"),
        List("Brandon Sanderson", "Second Author"), List("Michael Kramer", "Kate Reading"), List("Fantasy"),
        Text("The Stormlight Archive"), Text("1"), Text("Tor"), Text(releaseDate), Text("English"),
        Text("unabridged"), Text("B003P2WO5E"), Text(null));

    [Fact]
    public void Maps_the_passed_in_people_the_title_the_year_the_position_and_the_disc()
    {
        var tokens = BookTokens.For(Fields(), "Brandon Sanderson", "Michael Kramer", 3, 2);

        Assert.Equal("Brandon Sanderson", tokens["artist"]);
        Assert.Equal("Michael Kramer", tokens["narrator"]);
        Assert.Equal("The Way of Kings", tokens["album"]);
        Assert.Equal("The Way of Kings", tokens["title"]);
        Assert.Equal("The Stormlight Archive", tokens["series"]);
        Assert.Equal("1", tokens["series-part"]);
        Assert.Equal("2021", tokens["year"]);
        Assert.Equal("3", tokens["track"]);
        Assert.Equal("2", tokens["disc"]);
    }

    [Fact]
    public void A_release_date_without_a_leading_year_and_no_disc_give_null()
    {
        var tokens = BookTokens.For(Fields("unknown"), null, null, 1, null);

        Assert.Null(tokens["year"]);
        Assert.Null(tokens["disc"]);
        Assert.Null(tokens["artist"]);
        Assert.Null(tokens["narrator"]);
        Assert.Equal("1", tokens["track"]);
    }

    [Fact]
    public void Every_template_token_is_a_key()
    {
        var tokens = BookTokens.For(Fields(), "A", "N", 1, null);

        Assert.All(NamingTemplate.Tokens, token => Assert.True(tokens.ContainsKey(token), token));
    }
}
