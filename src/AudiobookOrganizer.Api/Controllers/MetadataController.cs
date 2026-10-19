using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/metadata")]
public class MetadataController(MetadataSearchService search) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<MetadataSearchResultDto>> Search(string? keywords, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            ModelState.AddModelError("keywords", "Enter a title, author, ASIN or ISBN to search for.");
            return ValidationProblem(ModelState);
        }

        try
        {
            return Ok(await search.SearchAsync(keywords.Trim(), ct));
        }
        catch (MetadataProviderException ex)
        {
            // Every provider failed; MetadataSearchService has logged each one.
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: ex.Message);
        }
    }
}
