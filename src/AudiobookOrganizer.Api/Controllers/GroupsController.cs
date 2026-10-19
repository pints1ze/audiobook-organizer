using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services.Grouping;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/groups")]
public class GroupsController(GroupingService grouping) : ControllerBase
{
    [HttpGet]
    public ActionResult<GroupListDto> Get() => grouping.List();

    [HttpPut("{id:long}")]
    public ActionResult<GroupSummaryDto> Put(long id, UpdateGroupDto request)
    {
        var result = grouping.Rename(id, request);
        if (result.Group is not null) return result.Group;
        if (result.Errors.Count == 0) return NotFound();

        foreach (var (field, messages) in result.Errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
