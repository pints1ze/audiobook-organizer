using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;
using AudiobookOrganizer.Api.Services.Grouping;

namespace AudiobookOrganizer.Api.Services;

/// <summary>Builds the review workspace's book list: every unprocessed book with its stage. Read-only.</summary>
public class ReviewService(
    GroupingService grouping, AudiobookService audiobooks, NamingPreviewService previews, ProcessingService processing,
    CoverService covers)
{
    public const string NeedsMatch = "NeedsMatch", NeedsReview = "NeedsReview", Ready = "Ready", Queued = "Queued",
        Failed = "Failed";

    public ReviewListDto List()
    {
        var preview = previews.PreviewAll().Books.ToDictionary(b => b.GroupId);
        var jobsByGroup = processing.GetJobs()
            .Where(j => j.GroupId is not null)
            .ToLookup(j => j.GroupId!.Value, j => (j.Id, j.Status));

        // shortcut: one metadata and cover read per book — revisit when the list is slow (hundreds of books)
        var books = grouping.List().Groups.Select(group =>
        {
            var fields = audiobooks.GetMetadata(group.Id)?.Fields;
            var hasMatch = audiobooks.GetMatch(group.Id).Match is not null;
            var hasUserValue = fields is not null && HasUserValue(fields);
            var stage = Stage(jobsByGroup[group.Id], hasMatch, hasUserValue, preview.GetValueOrDefault(group.Id));

            var cover = covers.GetCovers(group.Id);
            var (coverSource, coverFileId) =
                cover?.Chosen is not null ? ("chosen", (long?)null)
                : cover?.Embedded.Count > 0 ? ("embedded", cover.Embedded[0].FileId)
                : ((string?)null, (long?)null);

            return new ReviewBookDto(group.Id, fields?.Title.Value ?? group.Title,
                fields is null ? group.Author : fields.Authors.Value.FirstOrDefault(),
                fields?.Narrators.Value.FirstOrDefault(), group.TotalDurationMs, group.FileCount, stage,
                coverSource, coverFileId);
        }).ToList();

        int Count(string stage) => books.Count(b => b.Stage == stage);
        return new ReviewListDto(books,
            new ReviewCountsDto(Count(NeedsMatch), Count(NeedsReview), Count(Ready), Count(Queued), Count(Failed)));
    }

    /// <summary>
    /// A book's stage, first match wins: its newest job (highest id) is Queued or Running → Queued; Failed → Failed;
    /// no match and no user-entered value → NeedsMatch; a preview with no missing token, error, duplicate or existing
    /// destination → Ready; otherwise (including no preview) → NeedsReview.
    /// </summary>
    public static string Stage(
        IEnumerable<(long Id, string Status)> jobs, bool hasMatch, bool hasUserValue, BookPreviewDto? preview)
    {
        var newest = jobs.OrderByDescending(j => j.Id).Select(j => j.Status).FirstOrDefault();
        if (newest is ProcessingJob.Queued or ProcessingJob.Running) return Queued;
        if (newest is ProcessingJob.Failed) return Failed;
        if (!hasMatch && !hasUserValue) return NeedsMatch;
        var clean = preview is not null && preview.Files.All(f =>
            f.Missing.Count == 0 && f.Error is null && !f.Duplicate && !f.Exists);
        return clean ? Ready : NeedsReview;
    }

    private static bool HasUserValue(BookMetadataFieldsDto f) =>
        new[]
        {
            f.Title.Origin, f.Subtitle.Origin, f.Authors.Origin, f.Narrators.Origin, f.Genres.Origin, f.Series.Origin,
            f.SeriesPart.Origin, f.Publisher.Origin, f.ReleaseDate.Origin, f.Language.Origin, f.Format.Origin,
            f.Asin.Origin, f.Isbn.Origin,
        }.Contains("user");
}
