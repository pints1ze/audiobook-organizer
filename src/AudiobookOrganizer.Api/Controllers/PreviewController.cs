using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/preview")]
public class PreviewController(NamingPreviewService previews) : ControllerBase
{
    [HttpGet]
    public ActionResult<LibraryPreviewDto> Get() => previews.PreviewAll();
}
