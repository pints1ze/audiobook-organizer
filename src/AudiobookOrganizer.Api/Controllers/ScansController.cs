using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/scans")]
public class ScansController(ScanRunner runner, ScanRepository repository) : ControllerBase
{
    private const string LatestPath = "/api/scans/latest";

    [HttpPost]
    public IActionResult Start()
    {
        if (!runner.TryStart(out var run))
            return Problem(
                title: "A scan is already running.",
                detail: "Wait for the current scan to finish, then start another.",
                statusCode: StatusCodes.Status409Conflict);

        return Accepted(LatestPath, ScanRunDto.From(run));
    }

    [HttpGet("latest")]
    public IActionResult Latest()
    {
        var run = repository.GetLatestRun();
        return run is null ? NoContent() : Ok(ScanRunDto.From(run));
    }
}
