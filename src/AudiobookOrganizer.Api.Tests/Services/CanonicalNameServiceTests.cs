using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class CanonicalNameServiceTests
{
    [Theory]
    [InlineData("Stephen King")]
    [InlineData(" Stephen  King ")]
    [InlineData("KING, STEPHEN")]
    [InlineData("king,stephen")]
    [InlineData("Stephen\tKing")]
    public void NameKey_gives_one_key_for_spacing_case_and_last_first_variants(string name)
    {
        Assert.Equal("STEPHEN KING", CanonicalNameService.NameKey(name));
    }

    [Fact]
    public void NameKey_does_not_swap_a_name_with_two_commas()
    {
        Assert.Equal("KING, STEPHEN, JR.", CanonicalNameService.NameKey("King,  Stephen, Jr."));
    }

    [Fact]
    public void Clean_collapses_whitespace_but_keeps_case()
    {
        Assert.Equal("King, Stephen", CanonicalNameService.Clean("  King,   Stephen \t"));
    }

    [Theory]
    [InlineData("Science Fiction")]
    [InlineData("science-fiction")]
    [InlineData(" SCIENCE  FICTION ")]
    [InlineData("Science_Fiction")]
    [InlineData("Science/Fiction")]
    [InlineData("Science - Fiction")]
    public void GenreKey_gives_one_key_for_spacing_case_and_separator_variants(string genre)
    {
        Assert.Equal("SCIENCE FICTION", CanonicalNameService.GenreKey(genre));
    }

    [Fact]
    public void GenreKey_does_not_swap_around_a_comma()
    {
        Assert.Equal("KING, STEPHEN", CanonicalNameService.GenreKey("King, Stephen"));
    }

    [Fact]
    public void GenreKey_does_not_join_an_abbreviation_to_its_full_form()
    {
        Assert.Equal("SCI FI", CanonicalNameService.GenreKey("Sci-Fi"));
    }
}
