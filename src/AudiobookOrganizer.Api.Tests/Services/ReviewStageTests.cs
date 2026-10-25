using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;

namespace AudiobookOrganizer.Api.Tests.Services;

public class ReviewStageTests
{
    private static FilePreviewDto Clean(long fileId = 1) =>
        new(fileId, "/in/a.mp3", "A/a.mp3", "/lib/A/a.mp3", [], null);

    private static BookPreviewDto Preview(params FilePreviewDto[] files) => new(7, "/lib", "A", "B", files);

    private static readonly BookPreviewDto CleanPreview = Preview(Clean(1), Clean(2));

    [Theory]
    [InlineData("Queued", "Queued")]
    [InlineData("Running", "Queued")]
    [InlineData("Failed", "Failed")]
    public void The_newest_job_decides_before_metadata(string status, string expected)
    {
        Assert.Equal(expected, ReviewService.Stage([(1, status)], hasMatch: false, hasUserValue: false, CleanPreview));
    }

    [Fact]
    public void The_newest_job_wins_over_older_ones()
    {
        Assert.Equal("Failed", ReviewService.Stage([(1, "Queued"), (5, "Failed"), (3, "Succeeded")], true, false, CleanPreview));
        Assert.Equal("Queued", ReviewService.Stage([(9, "Queued"), (4, "Failed")], true, false, CleanPreview));
    }

    [Fact]
    public void No_match_and_no_user_value_needs_a_match()
    {
        Assert.Equal("NeedsMatch", ReviewService.Stage([], hasMatch: false, hasUserValue: false, CleanPreview));
    }

    [Fact]
    public void A_match_with_a_clean_preview_is_ready()
    {
        Assert.Equal("Ready", ReviewService.Stage([], hasMatch: true, hasUserValue: false, CleanPreview));
    }

    [Fact]
    public void A_user_edited_book_with_no_match_and_a_clean_preview_is_ready()
    {
        Assert.Equal("Ready", ReviewService.Stage([], hasMatch: false, hasUserValue: true, CleanPreview));
    }

    [Fact]
    public void A_match_with_any_preview_problem_needs_review()
    {
        var problems = new[]
        {
            Clean(2) with { Missing = ["series"] },
            Clean(2) with { RelativePath = null, DestinationPath = null, Error = "The library folder isn't set." },
            Clean(2) with { Duplicate = true },
            Clean(2) with { Exists = true },
        };
        Assert.All(problems, problem =>
            Assert.Equal("NeedsReview", ReviewService.Stage([], true, false, Preview(Clean(1), problem))));
        Assert.Equal("NeedsReview", ReviewService.Stage([], true, false, null));
    }
}
