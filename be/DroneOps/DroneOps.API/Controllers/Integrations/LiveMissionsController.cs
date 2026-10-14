using System.Security.Claims;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/live/missions")]
[Authorize(Roles = "Pilot,operation_manager")]
public class LiveMissionsController : ControllerBase
{
    private readonly ITelemetryIngestionService _telemetryService;

    public LiveMissionsController(ITelemetryIngestionService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// Lấy danh sách các nhiệm vụ đang bay / hoạt động cùng trạng thái kết nối trực tiếp của drone (Live Tracking)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<LiveMissionItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLiveMissions(CancellationToken cancellationToken)
    {
        try
        {
            var isManager = User.IsInRole("operation_manager");
            Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

            var result = await _telemetryService.GetLiveMissionsAsync(
                currentUserId,
                isManager,
                cancellationToken);

            return Ok(result);
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