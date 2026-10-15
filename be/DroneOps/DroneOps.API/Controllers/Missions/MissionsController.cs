using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/missions")]
[Authorize(Roles = "Pilot,operation_manager,uav_operator,operations_manager")]
public sealed class MissionsController : ControllerBase
{
    private readonly IMissionService _missionService;
    private readonly ITelemetryIngestionService _telemetryService;

    public MissionsController(
        IMissionService missionService,
        ITelemetryIngestionService telemetryService)
    {
        _missionService = missionService;
        _telemetryService = telemetryService;
    }


    [HttpGet]
    [Authorize(Roles = "operation_manager")]
    public async Task<IActionResult> GetAll(
     CancellationToken cancellationToken)
    {
        var result =
            await _missionService.GetAllAsync(
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("my")]
    [Authorize(Roles = "Pilot,uav_operator")]
    public async Task<IActionResult> GetMy(
    CancellationToken cancellationToken)
    {
        var userIdValue =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(
            userIdValue,
            out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _missionService.GetMyAsync(
                userId,
                cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Pilot,uav_operator")]
    [ProducesResponseType(typeof(MissionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MissionResponse>> CreateAsync(
        [FromBody] CreateMissionRequest request,
        CancellationToken cancellationToken)
    {
        var pilotIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(pilotIdValue, out var pilotId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var response = await _missionService.CreateAsync(
            pilotId,
            request,
            cancellationToken);

        return Created(
            $"/api/missions/{response.Id}",
            response);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "operation_manager")]
    public async Task<ActionResult<MissionResponse>> GetByIdAsync(
     Guid id,
     CancellationToken cancellationToken)
    {
        var result =
            await _missionService.GetByIdAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy snapshot viễn trắc (telemetry) hợp lệ mới nhất của một nhiệm vụ bay (SCR Live Monitoring)
    /// </summary>
    [HttpGet("{id:guid}/telemetry/latest")]
    [ProducesResponseType(typeof(MissionLatestTelemetryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatestTelemetryAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var isManager = User.IsInRole("operation_manager") || User.IsInRole("operations_manager");
            Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

            var result = await _telemetryService.GetLatestMissionTelemetryAsync(
                id,
                currentUserId,
                isManager,
                cancellationToken);

            if (result == null)
            {
                return NotFound(new { message = $"Mission with ID '{id}' was not found." });
            }

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}