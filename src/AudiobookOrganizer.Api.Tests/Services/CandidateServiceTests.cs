using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class CandidateServiceTests
{
    private const long TenHoursMs = 10 * 60 * 60 * 1000L;

    private static MetadataCandidateDto Candidate(string? title, string[] authors, int? runtimeMinutes) =>
        new("audible", "B0TEST0001", title, null, authors, [], null, null, null, null, runtimeMinutes, null, null);

    [Fact]
    public void Exact_title_author_and_runtime_score_100()
    {
        var score = CandidateService.Score("Project Hail Mary", ["Andy Weir"], TenHoursMs,
            Candidate("Project Hail Mary", ["Andy Weir"], 600));

        Assert.Equal(100, score);
    }

    [Theory]
    [InlineData(690, 75)] // 15% off
    [InlineData(null, 75)] // candidate has no runtime
    [InlineData(630, 87)] // 5% off: half the runtime points
    public void Runtime_points_drop_when_the_runtime_is_off_or_missing(int? runtimeMinutes, int expected)
    {
        var score = CandidateService.Score("Project Hail Mary", ["Andy Weir"], TenHoursMs,
            Candidate("Project Hail Mary", ["Andy Weir"], runtimeMinutes));

        Assert.Equal(expected, score);
    }

    [Fact]
    public void A_book_without_a_duration_gets_no_runtime_points()
    {
        Assert.Equal(75, CandidateService.Score("Project Hail Mary", ["Andy Weir"], 0,
            Candidate("Project Hail Mary", ["Andy Weir"], 600)));
    }

    [Fact]
    public void An_unrelated_title_gets_no_title_points()
    {
        var score = CandidateService.Score("Project Hail Mary", ["Andy Weir"], TenHoursMs,
            Candidate("Dungeon Crawler Carl", ["Matt Dinniman"], 600));

        Assert.Equal(25, score);
    }

    [Fact]
    public void Title_points_are_the_share_of_the_book_title_words_found()
    {
        var score = CandidateService.Score("Project Hail Mary", [], 0, Candidate("Project Mary", [], null));

        Assert.Equal(33, score); // 2 of 3 words
    }

    [Fact]
    public void Case_punctuation_and_last_first_authors_are_ignored()
    {
        var score = CandidateService.Score("project hail-mary!", ["WEIR, ANDY"], TenHoursMs,
            Candidate("Project: Hail Mary", ["Ryan Reader", "Andy Weir"], 600));

        Assert.Equal(100, score);
    }
}
