using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController(ProcessingService processing, ProcessingWorker worker) : ControllerBase
{
    [HttpPost]
    public ActionResult<ProcessingJobDto> Approve(ApproveRequest request)
    {
        if (request.GroupId is not > 0)
        {
            ModelState.AddModelError("groupId", "Choose a book to approve.");
            return ValidationProblem(ModelState);
        }

        var result = processing.Approve(request.GroupId.Value);
        if (result.Outcome == ApproveOutcome.Approved) worker.Wake();
        return result.Outcome switch
        {
            ApproveOutcome.Approved => CreatedAtAction(nameof(GetById), new { id = result.Job!.Id }, result.Job),
            ApproveOutcome.NotFound => NotFound(),
            ApproveOutcome.Conflict => Problem(
                title: "The book is already queued.",
                detail: "This book already has a queued or running job.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(title: "The book can't be approved yet.", detail: result.Message,
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<ProcessingJobDto>> List() => Ok(processing.GetJobs());

    [HttpGet("{id:long}")]
    public ActionResult<ProcessingJobDto> GetById(long id) => processing.GetJob(id) is { } job ? job : NotFound();
}
