using System.Security.Claims;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Missions;

[ApiController]
[Route("api/missions")]
[Authorize(Roles = "uav_operator,operation_manager")]
public sealed class MissionEvidenceController(
    IMissionValidationService validation,
    IMissionWeatherQueryService weather)
    : ControllerBase
{
    private Guid Actor =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : throw new System.Security.Authentication.AuthenticationException(
                "Invalid user identity.");

    private bool Manager =>
        User.IsInRole("operation_manager");

    // =========================================================
    // LATEST VALIDATION
    // =========================================================

    [HttpGet("{id:guid}/validation")]
    public async Task<IActionResult> Validation(
        Guid id,
        [FromQuery] bool latest,
        CancellationToken ct)
    {
        if (latest)
        {
            var latestResult =
                await validation.ReadLatestRunAsync(
                    id,
                    Actor,
                    Manager,
                    ct);

            return Ok(latestResult);
        }

        var result =
            await validation.ReadAsync(
                id,
                Actor,
                Manager,
                ct);

        return Ok(result);
    }

    // =========================================================
    // SPECIFIC VALIDATION RUN
    // =========================================================

    [HttpGet("{id:guid}/validation/runs/{runId:guid}")]
    public async Task<IActionResult> ValidationRun(
        Guid id,
        Guid runId,
        CancellationToken ct)
    {
        var result =
            await validation.ReadRunAsync(
                id,
                runId,
                Actor,
                Manager,
                ct);

        return Ok(result);
    }

    // =========================================================
    // WEATHER
    // =========================================================

    [HttpGet("{id:guid}/weather")]
    public async Task<IActionResult> Weather(
        Guid id,
        CancellationToken ct)
    {
        var result =
            await weather.ReadAsync(
                id,
                Actor,
                Manager,
                false,
                ct);

        return Ok(result);
    }

    // =========================================================
    // REFRESH WEATHER
    // =========================================================

    [HttpPost("{id:guid}/weather/refresh")]
    public async Task<IActionResult> RefreshWeather(
        Guid id,
        CancellationToken ct)
    {
        var result =
            await weather.ReadAsync(
                id,
                Actor,
                Manager,
                true,
                ct);

        return Ok(result);
    }
}