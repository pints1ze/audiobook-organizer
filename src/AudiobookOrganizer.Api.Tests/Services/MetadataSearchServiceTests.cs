using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class MetadataSearchServiceTests
{
    [Theory]
    [InlineData("9780593135204", "9780593135204")]
    [InlineData("978-0-593-13520-4", "9780593135204")]
    [InlineData(" 978 0 593 13520 4 ", "9780593135204")]
    [InlineData("0-593-13520-0", "0593135200")]
    [InlineData("080442957x", "080442957X")]
    public void Isbn_like_keywords_normalize(string keywords, string expected)
    {
        Assert.True(MetadataSearchService.TryNormalizeIsbn(keywords, out var isbn));
        Assert.Equal(expected, isbn);
    }

    [Theory]
    [InlineData("project hail mary")]
    [InlineData("B08G9PRS1K")]
    [InlineData("12345")]
    [InlineData("97805931352041")]
    [InlineData("X805931352")]
    [InlineData("978059313520X")]
    [InlineData("")]
    public void Other_keywords_are_not_isbns(string keywords)
    {
        Assert.False(MetadataSearchService.TryNormalizeIsbn(keywords, out _));
    }
}
