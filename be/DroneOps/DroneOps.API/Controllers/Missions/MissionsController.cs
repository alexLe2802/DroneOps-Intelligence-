using System.Security.Claims;
using DroneOps.Application.Services.Missions;
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
    private readonly MissionValidationService _validation;
    private readonly MissionWorkflowService _workflow;
    private readonly ITelemetryIngestionService _telemetryService;

    public MissionsController(
        IMissionService missionService,
        ITelemetryIngestionService telemetryService, MissionValidationService validation, MissionWorkflowService workflow)
    {
        _missionService = missionService;
        _validation = validation;
        _workflow = workflow;
        _telemetryService = telemetryService;
    }

    [HttpGet]
    [Authorize(Roles = "operation_manager,operations_manager")]
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
    [Authorize(Roles = "operation_manager,operations_manager,Pilot,uav_operator")]
    public async Task<ActionResult<MissionResponse>> GetByIdAsync(
     Guid id,
     CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var actor)) return Unauthorized();
        await _validation.ReadAsync(id,actor,User.IsInRole("operation_manager"),null,cancellationToken);
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

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "uav_operator")]
    public async Task<ActionResult<MissionResponse>>
    UpdateAsync(
        Guid id,
        [FromBody] CreateMissionRequest request,
        CancellationToken cancellationToken)
    {
        var userIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _missionService.UpdateAsync(
                id,
                userId,
                request,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// Gửi duyệt nhiệm vụ bay (Submit Mission)
    /// </summary>
    [HttpPut("{id:guid}/submit")]
    [Authorize(Roles = "uav_operator")]
    public IActionResult SubmitMission(Guid id)
        => Conflict(new { message = "Mission creation and edits now validate and submit automatically. Save the plan to send it for review." });

    /// <summary>
    /// Kiểm tra tính hợp lệ của tham số / waypoints nhiệm vụ bay (Validate Mission)
    /// </summary>
    [HttpPost("validate")]
    [Authorize(Roles = "uav_operator,operation_manager")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateMission([FromBody] MissionValidateDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var actor)) return Unauthorized();
        try
        {
            // Always load the saved plan; ignore client-supplied waypoint replacements.
            return Ok(await _validation.RevalidateAsync(request.MissionId, actor, User.IsInRole("operation_manager"), "Manager", cancellationToken, request.UpdateRequestId));
        }
        catch (UnauthorizedAccessException e) { return StatusCode(403, new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
    }

    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "uav_operator,operation_manager")]
    public async Task<IActionResult> StartMission(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var actor)) return Unauthorized();
        try { return Ok(await _workflow.StartAsync(id, actor, User.IsInRole("operation_manager"), ct)); }
        catch (UnauthorizedAccessException e) { return StatusCode(403, new { message = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (MissionValidationException) { throw; }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    /// <summary>
    /// Hủy chuyến bay / nhiệm vụ (Cancel Mission)
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "uav_operator,operation_manager")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelMission(Guid id, [FromBody] CancelMissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if(!TryGetUserId(out var actor))return Unauthorized();
            await _workflow.CancelAsync(id,actor,User.IsInRole("operation_manager"),request?.Reason??"",cancellationToken);
            return Ok(new { message = "Mission cancelled successfully." });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
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