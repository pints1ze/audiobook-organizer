using AudiobookOrganizer.Api.Services.Naming;

namespace AudiobookOrganizer.Api.Tests.Services;

public class NamingTemplateTests
{
    private const string SeriesDir = "%artist%/[%series%/Book %series-part:2% - ]%album%";

    private static Dictionary<string, string?> Values(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Series_aware_example_renders_exactly()
    {
        var values = Values(("artist", "Brandon Sanderson"), ("series", "Mistborn"), ("series-part", "1"),
            ("album", "The Final Empire"), ("track", "1"));

        var dir = NamingTemplate.Render(SeriesDir, values);
        var file = NamingTemplate.Render("[%track:3%. ]%album%", values);

        Assert.Equal("Brandon Sanderson/Mistborn/Book 01 - The Final Empire", dir.Path);
        Assert.Equal("001. The Final Empire", file.Path);
        Assert.Empty(dir.Missing);
        Assert.Empty(file.Missing);
        Assert.Null(dir.Error);
        Assert.Null(file.Error);
    }

    [Fact]
    public void Single_file_example_without_series_or_track_renders_exactly()
    {
        var values = Values(("artist", "Ernest Cline"), ("album", "Ready Player One"));

        var dir = NamingTemplate.Render(SeriesDir, values);
        var file = NamingTemplate.Render("[%track:3%. ]%album% (%artist%)", values);

        Assert.Equal("Ernest Cline/Ready Player One", dir.Path);
        Assert.Equal("Ready Player One (Ernest Cline)", file.Path);
        Assert.Empty(dir.Missing);
        Assert.Empty(file.Missing);
    }

    [Fact]
    public void Keys_are_case_insensitive_and_whitespace_values_count_as_empty()
    {
        var result = NamingTemplate.Render("%ARTIST%[ - %narrator%]", Values(("Artist", "A"), ("NARRATOR", "   ")));

        Assert.Equal("A", result.Path);
        Assert.Empty(result.Missing);
    }

    [Theory]
    [InlineData("%track:3%", "track", "7", "007")]
    [InlineData("%series-part:2%", "series-part", "1.5", "01.5")]
    [InlineData("%track:3%", "track", "Prologue", "Prologue")]
    [InlineData("%track:2%", "track", "123", "123")]
    public void Padding_left_pads_the_leading_integer_part(string template, string key, string value, string expected)
    {
        var result = NamingTemplate.Render(template, Values((key, value)));

        Assert.Equal(expected, result.Path);
    }

    [Fact]
    public void Required_token_with_no_value_is_listed_in_missing_and_still_renders()
    {
        var result = NamingTemplate.Render("%album% - %narrator% - %narrator% - %year%",
            Values(("album", "Dune")));

        Assert.Equal("Dune - - -", result.Path);
        Assert.Equal(["narrator", "year"], result.Missing);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Token_inside_a_dropped_section_is_not_missing()
    {
        var result = NamingTemplate.Render("%album%[ (%series% %series-part%)]",
            Values(("album", "Dune"), ("series", "Dune Chronicles")));

        Assert.Equal("Dune", result.Path);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void Section_with_no_tokens_is_always_kept()
    {
        var result = NamingTemplate.Render("%album%[ (unabridged)]", Values(("album", "Dune")));

        Assert.Equal("Dune (unabridged)", result.Path);
    }

    [Theory]
    [InlineData("%author%")]
    [InlineData("%")]
    [InlineData("%album")]
    [InlineData("%%")]
    [InlineData("%track:0%")]
    [InlineData("%track:10%")]
    [InlineData("%track:x%")]
    [InlineData("[a")]
    [InlineData("a]")]
    [InlineData("[[%series%]]")]
    [InlineData("../%album%")]
    [InlineData("%artist%/./x")]
    [InlineData("%artist%\\..\\x")]
    [InlineData("")]
    [InlineData("   ")]
    public void Template_errors_give_an_error_and_no_path(string template)
    {
        var result = NamingTemplate.Render(template, Values(("album", "Dune"), ("artist", "Frank Herbert"),
            ("series", "Dune"), ("track", "1")));

        Assert.NotNull(result.Error);
        Assert.Null(result.Path);
        Assert.Empty(result.Missing);
    }

    [Theory]
    [InlineData("%artist%/%album%", "AC/DC", "AC-DC/x")]
    [InlineData("%artist%/%album%", "AC\\DC", "AC-DC/x")]
    [InlineData("%artist%/%album%", "What? Why: Now*", "What- Why- Now-/x")]
    [InlineData("%artist%/%album%", "Dr. Who...", "Dr. Who/x")]
    [InlineData("%artist%/%album%", "  Lots   of\u00a0 space  ", "Lots of space/x")]
    [InlineData("%artist%/%album%", "a<b>c\"d|e\u0001f\tg", "a-b-c-d-e-f-g/x")]
    [InlineData("%artist%/%album%", "CON", "CON_/x")]
    [InlineData("%artist%/%album%", "nul.txt", "nul_.txt/x")]
    [InlineData("%artist%/%album%", "com1", "com1_/x")]
    [InlineData("%artist%/%album%", "Lpt9.tar.gz", "Lpt9_.tar.gz/x")]
    [InlineData("%artist%/%album%", "CONSOLE", "CONSOLE/x")]
    [InlineData("%artist%/%album%", "..", "x")]
    [InlineData("%artist%/%album%", "   ", "x")]
    [InlineData("%artist%//%album%", "a", "a/x")]
    [InlineData("%artist%: %album%", "a", "a- x")]
    public void Segments_are_sanitized(string template, string artist, string expected)
    {
        var result = NamingTemplate.Render(template, Values(("artist", artist), ("album", "x")));

        Assert.Equal(expected, result.Path);
    }

    [Fact]
    public void Segments_are_normalized_to_nfc()
    {
        var result = NamingTemplate.Render("%artist%", Values(("artist", "Beyonce\u0301")));

        Assert.Equal("Beyonc\u00e9", result.Path);
    }

    [Fact]
    public void Long_segment_is_cut_to_200_characters()
    {
        var result = NamingTemplate.Render("%album%", Values(("album", new string('a', 300))));

        Assert.Equal(new string('a', 200), result.Path);
    }

    [Fact]
    public void Cut_never_splits_a_surrogate_pair()
    {
        var result = NamingTemplate.Render("%album%", Values(("album", new string('a', 199) + "\U0001F600tail")));

        Assert.Equal(new string('a', 199), result.Path);
    }

    [Fact]
    public void Template_that_renders_only_empty_segments_is_an_error()
    {
        var result = NamingTemplate.Render("[%series%]/[%series-part%]", Values());

        Assert.Equal("The template produced an empty path.", result.Error);
        Assert.Null(result.Path);
    }

    [Fact]
    public void ResolveUnder_returns_a_full_path_inside_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "naming-root");

        var full = NamingTemplate.ResolveUnder(root, "Brandon Sanderson/Mistborn/001. The Final Empire.mp3");

        Assert.NotNull(full);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "Brandon Sanderson", "Mistborn", "001. The Final Empire.mp3")),
            full);
    }

    [Fact]
    public void ResolveUnder_accepts_a_root_with_a_trailing_separator()
    {
        var root = Path.Combine(Path.GetTempPath(), "naming-root") + Path.DirectorySeparatorChar;

        Assert.NotNull(NamingTemplate.ResolveUnder(root, "a/b"));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("/etc/x")]
    [InlineData("\\etc\\x")]
    [InlineData("C:\\x")]
    [InlineData("c:x")]
    [InlineData("\\\\server\\x")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("../naming-root-other/x")]
    public void ResolveUnder_refuses_paths_outside_root(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "naming-root");

        Assert.Null(NamingTemplate.ResolveUnder(root, relative));
    }

    [Fact]
    public void ResolveUnder_refuses_a_sibling_that_shares_the_root_prefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lib");

        Assert.Null(NamingTemplate.ResolveUnder(root, "../Library/x"));
    }

    [Fact]
    public void Render_output_always_resolves_under_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "naming-root");
        var result = NamingTemplate.Render("%artist%/%album%", Values(("artist", ".."), ("album", "../../etc")));

        Assert.NotNull(result.Path);
        Assert.NotNull(NamingTemplate.ResolveUnder(root, result.Path));
    }
}
