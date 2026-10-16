using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Missions;

[ApiController]
[Route("api/missions")]
[Authorize(Roles = "operation_manager,operations_manager")]
public class MissionApprovalsController : ControllerBase
{
    private readonly IMissionApprovalService _approvalService;

    public MissionApprovalsController(IMissionApprovalService approvalService)
    {
        _approvalService = approvalService;
    }

    /// <summary>
    /// Lấy danh sách nhiệm vụ đang chờ phê duyệt (SCR-09 / UC-09 / KAN-35)
    /// </summary>
    [HttpGet("pending-approvals")]
    [ProducesResponseType(typeof(PagedPendingApprovalsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] GetPendingApprovalsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var managerId))
            {
                return Unauthorized(new { message = "Invalid authenticated user." });
            }

            var result = await _approvalService.GetPendingApprovalsAsync(request, managerId, cancellationToken);
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to load pending approvals." });
        }
    }

    /// <summary>
    /// Phê duyệt kế hoạch bay (SCR-09 / UC-09 / KAN-35)
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(MissionDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveMission(Guid id, [FromBody] ApproveMissionRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var managerId))
            {
                return Unauthorized(new { message = "Invalid authenticated user." });
            }

            var result = await _approvalService.ApproveMissionAsync(id, request ?? new ApproveMissionRequest(), managerId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to approve mission." });
        }
    }

    /// <summary>
    /// Từ chối kế hoạch bay kèm lý do bắt buộc (SCR-09 / UC-09 / KAN-35)
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(MissionDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectMission(Guid id, [FromBody] RejectMissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryGetUserId(out var managerId))
            {
                return Unauthorized(new { message = "Invalid authenticated user." });
            }

            var result = await _approvalService.RejectMissionAsync(id, request, managerId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to reject mission." });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}