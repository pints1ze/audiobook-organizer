using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/people")]
public class PeopleController(PersonService people) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<PersonDto> List() => people.List();

    [HttpGet("resolve")]
    public ActionResult<PersonDto> Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Enter a name to resolve.");
            return ValidationProblem(ModelState);
        }
        var person = people.Resolve(name);
        if (person is null) return NotFound();
        return person;
    }

    [HttpPost]
    public ActionResult<PersonDto> Create(PersonRequest request)
    {
        var result = people.Create(request);
        if (result.Saved is not null) return CreatedAtAction(nameof(List), null, result.Saved);
        return SaveFailed(result);
    }

    [HttpPut("{id:long}")]
    public ActionResult<PersonDto> Update(long id, PersonRequest request)
    {
        var result = people.Update(id, request);
        if (result.Saved is not null) return result.Saved;
        return SaveFailed(result);
    }

    private ActionResult SaveFailed(PersonSaveResult result)
    {
        if (result.NotFound) return NotFound();
        foreach (var (field, messages) in result.Errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
