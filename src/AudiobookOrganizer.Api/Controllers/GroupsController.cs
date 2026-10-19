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
}
