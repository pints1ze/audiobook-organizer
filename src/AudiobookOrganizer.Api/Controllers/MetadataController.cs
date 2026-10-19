using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/metadata")]
public class MetadataController(IMetadataProvider provider, ILogger<MetadataController> logger) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<MetadataCandidateDto>>> Search(string? keywords, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            ModelState.AddModelError("keywords", "Enter a title, author or ASIN to search for.");
            return ValidationProblem(ModelState);
        }

        try
        {
            return Ok(await provider.SearchAsync(keywords.Trim(), ct));
        }
        catch (MetadataProviderException ex)
        {
            logger.LogWarning(ex, "Metadata search with {Provider} failed for {Keywords}", provider.Name, keywords);
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: ex.Message);
        }
    }
}
