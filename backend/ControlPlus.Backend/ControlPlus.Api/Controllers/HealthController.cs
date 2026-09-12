using ControlPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            service = "ControlPlus.Api",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("database")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetDatabaseStatus(
        [FromServices] ControlPlusDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? Ok(new { status = "healthy", dependency = "postgresql" })
                : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy", dependency = "postgresql" });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy", dependency = "postgresql" });
        }
    }
}
