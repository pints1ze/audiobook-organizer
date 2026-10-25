using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/review")]
public class ReviewController(ReviewService review, CandidateService candidates) : ControllerBase
{
    public const int MaxGroupIds = 500;

    [HttpGet]
    public ActionResult<ReviewListDto> Get() => review.List();

    /// <summary>Accepts the best stored candidate of each listed book; see <see cref="CandidateService.AcceptSuggested"/>.</summary>
    [HttpPost("accept-suggested")]
    public ActionResult<AcceptSuggestedResponseDto> AcceptSuggested(AcceptSuggestedDto body)
    {
        if (body.GroupIds is not { Count: > 0 and <= MaxGroupIds } groupIds)
        {
            ModelState.AddModelError("groupIds", $"Choose between 1 and {MaxGroupIds} books.");
            return ValidationProblem(ModelState);
        }
        return new AcceptSuggestedResponseDto(candidates.AcceptSuggested(groupIds));
    }
}
