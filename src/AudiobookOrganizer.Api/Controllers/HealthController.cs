using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookOrganizer.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(SqliteConnectionFactory connections, ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            using var connection = connections.Open();
            await connection.ExecuteScalarAsync<int>("SELECT 1");
            return Ok(new HealthResponse("ok", "ok"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check database query failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("degraded", "error"));
        }
    }
}
