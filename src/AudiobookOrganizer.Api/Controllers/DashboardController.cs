using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(SettingsService settings, ScanRepository repository) : ControllerBase
{
    [HttpGet]
    public ActionResult<DashboardDto> Get()
    {
        var saved = settings.Get();
        var latest = repository.GetLatestRun();
        var completed = repository.GetLatestCompletedRun();
        var (files, errors) = completed is null ? (0, 0) : repository.CountFilesSeenIn(completed.Id);
        return new DashboardDto(saved.IncomingPath, saved.LibraryPath,
            latest is null ? null : ScanRunDto.From(latest), files, errors);
    }
}
