using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class CanonicalSuggestionsTests
{
    private static CanonicalEntryDto Entry(int id, string name, params string[] aliases) => new(id, name, aliases);

    private static IReadOnlyList<MergeSuggestionDto> People(params CanonicalEntryDto[] entries) =>
        CanonicalSuggestions.Suggest(CanonicalKind.Person, entries);

    private static IReadOnlyList<MergeSuggestionDto> Genres(params CanonicalEntryDto[] entries) =>
        CanonicalSuggestions.Suggest(CanonicalKind.Genre, entries);

    [Fact]
    public void Empty_catalog_gives_no_suggestions()
    {
        Assert.Empty(People());
        Assert.Empty(Genres());
    }

    [Fact]
    public void Names_that_differ_only_in_spacing_or_punctuation_are_suggested()
    {
        var dotted = Entry(1, "J.R.R. Tolkien");
        var spaced = Entry(2, "J. R. R. Tolkien");

        var suggestion = Assert.Single(People(dotted, spaced));

        Assert.Equal(1, suggestion.Keep.Id);
        Assert.Equal(2, suggestion.Merge.Id);
        Assert.Equal("\"J.R.R. Tolkien\" and \"J. R. R. Tolkien\" differ only in spacing or punctuation.",
            suggestion.Reason);
    }

    [Fact]
    public void Genres_that_differ_only_in_punctuation_are_suggested()
    {
        var suggestion = Assert.Single(Genres(Entry(1, "SciFi"), Entry(2, "Sci-Fi")));

        Assert.Equal("\"SciFi\" and \"Sci-Fi\" differ only in spacing or punctuation.", suggestion.Reason);
    }

    [Fact]
    public void A_match_through_an_alias_is_suggested_with_the_matching_texts()
    {
        var king = Entry(1, "Stephen King", "Richard Bachman");
        var bachman = Entry(2, "Richard-Bachman");

        var suggestion = Assert.Single(People(king, bachman));

        Assert.Equal(1, suggestion.Keep.Id);
        Assert.Equal(["Richard Bachman"], suggestion.Keep.Aliases);
        Assert.Equal("\"Richard Bachman\" and \"Richard-Bachman\" differ only in spacing or punctuation.",
            suggestion.Reason);
    }

    [Fact]
    public void Unrelated_names_are_not_suggested()
    {
        Assert.Empty(People(Entry(1, "Stephen King"), Entry(2, "Neil Gaiman"), Entry(3, "Andy Weir")));
        Assert.Empty(Genres(Entry(1, "Fantasy"), Entry(2, "Horror")));
    }

    [Fact]
    public void Each_pair_appears_once_even_when_several_names_match()
    {
        var a = Entry(1, "J.R.R. Tolkien", "J R R Tolkien");
        var b = Entry(2, "J. R. R. Tolkien", "JRR Tolkien", "John Ronald Reuel Tolkien");

        var suggestion = Assert.Single(People(a, b));

        Assert.EndsWith("differ only in spacing or punctuation.", suggestion.Reason);
    }

    [Theory]
    [InlineData("S. King", "Stephen King")]
    [InlineData("S King", "Stephen King")]
    [InlineData("J R R Tolkien", "John Ronald Reuel Tolkien")]
    [InlineData("John R. R. Tolkien", "John Ronald Reuel Tolkien")]
    public void Names_with_initials_are_suggested_for_people(string initials, string full)
    {
        var suggestion = Assert.Single(People(Entry(1, full), Entry(2, initials)));

        Assert.Equal($"\"{initials}\" may be \"{full}\" with initials.", suggestion.Reason);
    }

    [Theory]
    [InlineData("Stephen King", "Tabitha King")]
    [InlineData("S. King", "Stephen Edwin King")]
    [InlineData("S. King", "T. King")]
    [InlineData("S. King", "Stephen Gaiman")]
    public void Names_that_are_not_initials_of_each_other_are_not_suggested(string first, string second)
    {
        Assert.Empty(People(Entry(1, first), Entry(2, second)));
    }

    [Fact]
    public void Initials_match_through_an_alias()
    {
        var suggestion = Assert.Single(People(Entry(1, "Richard Bachman", "Stephen King"), Entry(2, "S. King")));

        Assert.Equal("\"S. King\" may be \"Stephen King\" with initials.", suggestion.Reason);
    }

    [Fact]
    public void The_initials_rule_does_not_apply_to_genres()
    {
        Assert.Empty(Genres(Entry(1, "S. Fiction"), Entry(2, "Science Fiction")));
    }

    [Fact]
    public void Keep_is_the_entry_with_more_aliases_then_the_lower_id()
    {
        var byAliases = Assert.Single(People(Entry(1, "Stephen King"), Entry(2, "S. King", "S King Author")));
        Assert.Equal(2, byAliases.Keep.Id);
        Assert.Equal(1, byAliases.Merge.Id);

        var byId = Assert.Single(People(Entry(7, "Stephen King"), Entry(3, "S. King")));
        Assert.Equal(3, byId.Keep.Id);
        Assert.Equal(7, byId.Merge.Id);
    }

    [Fact]
    public void Results_are_ordered_by_keep_then_merge_name_ignoring_case()
    {
        var suggestions = People(
            Entry(1, "stephen King"),
            Entry(2, "S. King"),
            Entry(3, "neil Gaiman"),
            Entry(4, "N. Gaiman"),
            Entry(5, "Neil-Gaiman"));

        Assert.Equal(
            [("neil Gaiman", "N. Gaiman"), ("neil Gaiman", "Neil-Gaiman"), ("stephen King", "S. King")],
            suggestions.Select(s => (s.Keep.Name, s.Merge.Name)));
    }

    [Fact]
    public void Result_does_not_depend_on_input_order()
    {
        var entries = new[] { Entry(1, "Stephen King"), Entry(2, "S. King"), Entry(3, "Stephen-King") };

        var forward = People(entries);
        var backward = People(entries.Reverse().ToArray());

        Assert.Equal(forward.Select(s => (s.Keep.Id, s.Merge.Id, s.Reason)),
            backward.Select(s => (s.Keep.Id, s.Merge.Id, s.Reason)));
    }

    [Fact]
    public void ForName_suggests_a_same_letters_match_and_an_initials_match_with_reasons()
    {
        var tolkien = Entry(1, "JRR Tolkien");
        var sanderson = Entry(2, "Brandon Sanderson");
        var entries = new[] { sanderson, tolkien };

        var same = Assert.Single(CanonicalSuggestions.ForName(CanonicalKind.Person, "J.R.R. Tolkien", entries));
        var initials = Assert.Single(CanonicalSuggestions.ForName(CanonicalKind.Person, "B. Sanderson", entries));

        Assert.Equal(1, same.Entry.Id);
        Assert.Equal("\"J.R.R. Tolkien\" and \"JRR Tolkien\" differ only in spacing or punctuation.", same.Reason);
        Assert.Equal(2, initials.Entry.Id);
        Assert.Equal("\"B. Sanderson\" may be \"Brandon Sanderson\" with initials.", initials.Reason);
    }

    [Fact]
    public void ForName_returns_nothing_for_an_unrelated_name()
    {
        Assert.Empty(CanonicalSuggestions.ForName(CanonicalKind.Person, "Neil Gaiman",
            [Entry(1, "Stephen King"), Entry(2, "Brandon Sanderson")]));
    }

    [Fact]
    public void ForName_matches_an_alias_and_orders_by_entry_name()
    {
        var entries = new[] { Entry(1, "Stephen King", "Richard Bachman"), Entry(2, "Rick Bachman") };

        var suggestions = CanonicalSuggestions.ForName(CanonicalKind.Person, "R. Bachman", entries);

        Assert.Equal([2, 1], suggestions.Select(s => s.Entry.Id));
        Assert.Equal("\"R. Bachman\" may be \"Richard Bachman\" with initials.", suggestions[1].Reason);
    }

    [Fact]
    public void ForName_gives_genres_only_the_same_letters_rule()
    {
        var entries = new[] { Entry(1, "S. Fiction"), Entry(2, "Sciencefiction") };

        var suggestion = Assert.Single(CanonicalSuggestions.ForName(CanonicalKind.Genre, "Science Fiction", entries));

        Assert.Equal(2, suggestion.Entry.Id);
    }
}
