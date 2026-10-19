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

    private ActionResult Invalid(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
