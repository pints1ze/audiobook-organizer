using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class PersonServiceTests
{
    [Theory]
    [InlineData("Stephen King")]
    [InlineData(" Stephen  King ")]
    [InlineData("KING, STEPHEN")]
    [InlineData("king,stephen")]
    [InlineData("Stephen\tKing")]
    public void NameKey_gives_one_key_for_spacing_case_and_last_first_variants(string name)
    {
        Assert.Equal("STEPHEN KING", PersonService.NameKey(name));
    }

    [Fact]
    public void NameKey_does_not_swap_a_name_with_two_commas()
    {
        Assert.Equal("KING, STEPHEN, JR.", PersonService.NameKey("King,  Stephen, Jr."));
    }

    [Fact]
    public void Clean_collapses_whitespace_but_keeps_case()
    {
        Assert.Equal("King, Stephen", PersonService.Clean("  King,   Stephen \t"));
    }
}
