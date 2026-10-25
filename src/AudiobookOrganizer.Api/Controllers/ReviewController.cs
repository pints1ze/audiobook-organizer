using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/review")]
public class ReviewController(ReviewService review) : ControllerBase
{
    [HttpGet]
    public ActionResult<ReviewListDto> Get() => review.List();
}
