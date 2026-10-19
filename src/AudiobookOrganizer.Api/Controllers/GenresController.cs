using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/genres")]
public class GenresController(CanonicalNameService names) : ControllerBase
{
    private const CanonicalKind Kind = CanonicalKind.Genre;

    [HttpGet]
    public IReadOnlyList<CanonicalEntryDto> List() => names.List(Kind);

    [HttpGet("suggestions")]
    public IReadOnlyList<MergeSuggestionDto> Suggestions() => names.Suggestions(Kind);

    [HttpGet("resolve")]
    public ActionResult<CanonicalEntryDto> Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Enter a genre to resolve.");
            return ValidationProblem(ModelState);
        }
        var entry = names.Resolve(Kind, name);
        if (entry is null) return NotFound();
        return entry;
    }

    [HttpPost]
    public ActionResult<CanonicalEntryDto> Create(CanonicalEntryRequest request)
    {
        var result = names.Create(Kind, request);
        if (result.Saved is not null) return CreatedAtAction(nameof(List), null, result.Saved);
        return SaveFailed(result);
    }

    [HttpPut("{id:long}")]
    public ActionResult<CanonicalEntryDto> Update(long id, CanonicalEntryRequest request)
    {
        var result = names.Update(Kind, id, request);
        if (result.Saved is not null) return result.Saved;
        return SaveFailed(result);
    }

    [HttpPost("{id:long}/merge")]
    public ActionResult<CanonicalEntryDto> Merge(long id, MergeRequest request)
    {
        var result = names.Merge(Kind, id, request.FromId);
        if (result.Saved is not null) return result.Saved;
        return SaveFailed(result);
    }

    private ActionResult SaveFailed(CanonicalSaveResult result)
    {
        if (result.NotFound) return NotFound();
        foreach (var (field, messages) in result.Errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
