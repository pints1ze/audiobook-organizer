using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using AudiobookOrganizer.Api.Services.Grouping;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(GroupingService grouping, AudiobookService audiobooks, CoverService covers,
    NamingPreviewService previews, ILogger<GroupsController> logger) : ControllerBase
{
    [HttpGet("{id:long}/preview")]
    public ActionResult<BookPreviewDto> GetPreview(long id) => previews.Preview(id) is { } preview ? preview : NotFound();

    [HttpGet("{id:long}/cover")]
    public ActionResult<BookCoverDto> GetCover(long id) => covers.GetCovers(id) is { } cover ? cover : NotFound();

    [HttpPut("{id:long}/cover")]
    public async Task<ActionResult<BookCoverDto>> PutCover(long id, ChooseCoverDto request, CancellationToken ct)
    {
        ChooseCoverResult result;
        try
        {
            result = await covers.ChooseCoverAsync(id, request, ct);
        }
        catch (CoverDownloadException ex)
        {
            logger.LogWarning(ex, "Cover download for group {GroupId} failed", id);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                detail: "The cover couldn't be downloaded. Try again later.");
        }
        if (result.Cover is not null) return result.Cover;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    private const long MaxUploadRequestBytes = CoverService.MaxCoverBytes + 64 * 1024;

    [HttpPut("{id:long}/cover/upload")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<BookCoverDto>> PutCoverUpload(long id, IFormFile? file, CancellationToken ct)
    {
        var image = file is null ? [] : await ReadAtMost(file, CoverService.MaxCoverBytes + 1, ct);
        var result = covers.SaveUpload(id, image);
        if (result.Cover is not null) return result.Cover;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    /// <summary>The file's first <paramref name="limit"/> bytes at most, so an oversized part is never held whole.</summary>
    private static async Task<byte[]> ReadAtMost(IFormFile file, int limit, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var buffer = new byte[Math.Min(file.Length, limit)];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    [HttpGet("{id:long}/cover/image")]
    public IActionResult GetCoverImage(long id) =>
        covers.GetChosenImage(id) is { } cover ? File(cover.Image, cover.MimeType) : NotFound();

    [HttpGet("{id:long}/files/{fileId:long}/cover")]
    public IActionResult GetFileCover(long id, long fileId) =>
        covers.ReadEmbedded(id, fileId) is { } picture ? File(picture.Data, picture.MimeType) : NotFound();

    [HttpGet("{id:long}/match")]
    public ActionResult<BookMatchDto> GetMatch(long id)
    {
        var result = audiobooks.GetMatch(id);
        if (!result.GroupFound) return NotFound();
        return result.Match is { } match ? match : NoContent();
    }

    [HttpPut("{id:long}/match")]
    public ActionResult<BookMatchDto> PutMatch(long id, MetadataCandidateDto candidate)
    {
        var result = audiobooks.PickMatch(id, candidate);
        if (result.Match is not null) return result.Match;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    [HttpGet("{id:long}/metadata")]
    public ActionResult<BookMetadataDto> GetMetadata(long id) =>
        audiobooks.GetMetadata(id) is { } metadata ? metadata : NotFound();

    [HttpGet("{id:long}/people")]
    public ActionResult<BookPeopleDto> GetPeople(long id) =>
        audiobooks.GetPeople(id) is { } people ? people : NotFound();

    [HttpGet("{id:long}/genres")]
    public ActionResult<BookGenresDto> GetGenres(long id) =>
        audiobooks.GetGenres(id) is { } genres ? genres : NotFound();

    [HttpPut("{id:long}/metadata")]
    public ActionResult<BookMetadataDto> PutMetadata(long id, UpdateBookMetadataDto request)
    {
        var result = audiobooks.UpdateMetadata(id, request);
        if (result.Metadata is not null) return result.Metadata;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    [HttpGet]
    public ActionResult<GroupListDto> Get() => grouping.List();

    [HttpGet("{id:long}")]
    public ActionResult<GroupDetailDto> GetById(long id) => grouping.GetDetail(id) is { } detail ? detail : NotFound();

    [HttpPut("{id:long}")]
    public ActionResult<GroupSummaryDto> Put(long id, UpdateGroupDto request)
    {
        var result = grouping.Rename(id, request);
        if (result.Group is not null) return result.Group;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    [HttpPut("{id:long}/order")]
    public ActionResult<GroupDetailDto> PutOrder(long id, ReorderGroupDto request)
    {
        var result = grouping.Reorder(id, request);
        if (result.Group is not null) return result.Group;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    [HttpPost]
    public ActionResult<GroupDetailDto> Post(CreateGroupDto request)
    {
        var result = grouping.CreateGroup(request);
        if (result.Group is not null) return CreatedAtAction(nameof(GetById), new { id = result.Group.Id }, result.Group);
        return Invalid(result.Errors);
    }

    [HttpPost("{id:long}/files")]
    public ActionResult<GroupDetailDto> PostFiles(long id, MoveFilesDto request)
    {
        var result = grouping.MoveFiles(id, request);
        if (result.Group is not null) return result.Group;
        return result.Errors.Count == 0 ? NotFound() : Invalid(result.Errors);
    }

    [HttpPost("excluded")]
    public ActionResult<GroupListDto> PostExcluded(MoveFilesDto request)
    {
        var result = grouping.Exclude(request);
        return result.List is not null ? result.List : Invalid(result.Errors);
    }

    [HttpPost("unresolved")]
    public ActionResult<GroupListDto> PostUnresolved(MoveFilesDto request)
    {
        var result = grouping.Unresolve(request);
        return result.List is not null ? result.List : Invalid(result.Errors);
    }

    private ActionResult Invalid(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
