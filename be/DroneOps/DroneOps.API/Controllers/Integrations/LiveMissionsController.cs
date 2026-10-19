using System.Security.Claims;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Integrations;

[ApiController]
[Route("api/live/missions")]
[Authorize(Roles = "Pilot,uav_operator,operation_manager,operations_manager")]
public class LiveMissionsController : ControllerBase
{
    private readonly ITelemetryIngestionService _telemetryService;

    public LiveMissionsController(ITelemetryIngestionService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// Lấy danh sách các nhiệm vụ đang bay / hoạt động cùng trạng thái kết nối trực tiếp của drone (Live Tracking).
    /// - Operation Manager: Xem và giám sát tất cả các chuyến bay.
    /// - Pilot (uav_operator): Chỉ xem và giám sát chuyến bay do chính Pilot đó đăng ký.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<LiveMissionItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLiveMissions(CancellationToken cancellationToken)
    {
        try
        {
            var isManager = IsOperationManager();
            Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

            // Nếu là Pilot nhưng không xác định được UserId hợp lệ thì chặn truy cập
            if (!isManager && !currentUserId.HasValue)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Forbidden: Unable to verify pilot identity."
                });
            }

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

    private bool IsOperationManager()
    {
        if (User.IsInRole("operation_manager") || User.IsInRole("operations_manager"))
        {
            return true;
        }

        var roleClaims = User.FindAll(ClaimTypes.Role)
            .Concat(User.FindAll("role"))
            .Select(c => c.Value);

        return roleClaims.Any(r =>
            string.Equals(r, "operation_manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, "operations_manager", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}