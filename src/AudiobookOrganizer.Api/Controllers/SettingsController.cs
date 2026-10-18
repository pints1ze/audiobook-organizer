using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    public ActionResult<SettingsDto> Get() => settings.Get();

    [HttpPut]
    public ActionResult<SettingsDto> Put(SettingsDto request)
    {
        var result = settings.Save(request);
        if (result.Saved is not null) return result.Saved;

        foreach (var (field, messages) in result.Errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
