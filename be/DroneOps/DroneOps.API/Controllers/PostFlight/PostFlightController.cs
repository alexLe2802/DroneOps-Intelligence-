using System.Security.Claims;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.PostFlight;

[ApiController]
[Route("api/flight-history")]
[Authorize(Roles = "Pilot,uav_operator,operation_manager,operations_manager")]
public class PostFlightController : ControllerBase
{
    private readonly IPostFlightService _service;

    public PostFlightController(IPostFlightService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách lịch sử các chuyến bay (SCR-13 / UC-18)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFlightHistory([FromQuery] GetFlightHistoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var isManager = IsManager();
            Guid? pilotUserId = isManager ? null : GetRequiredUserId();

            var result = await _service.GetFlightHistoryAsync(request, pilotUserId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    /// <summary>
    /// Chốt hoàn tất chuyến bay và tự động lập Post-Flight Report (UC-17)
    /// </summary>
    [HttpPost("{missionId:guid}/finalize")]
    public async Task<IActionResult> FinalizeFlight(Guid missionId, [FromBody] FinalizeFlightRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = TryGetUserId(out var userId) ? userId : (Guid?)null;
            var result = await _service.FinalizeAndGenerateReportAsync(
                missionId, request, currentUserId, IsManager(), cancellationToken);
            return Ok(new
            {
                Success = true,
                Message = "Flight mission finalized and post-flight report generated successfully.",
                Data = result
            });
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    /// <summary>
    /// Xem chi tiết báo cáo phân tích sau bay (SCR-14 / UC-18)
    /// </summary>
    [HttpGet("{missionId:guid}/report")]
    public async Task<IActionResult> GetReport(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = TryGetUserId(out var userId) ? userId : (Guid?)null;
            var result = await _service.GetReportByMissionIdAsync(
                missionId, currentUserId, IsManager(), cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    /// <summary>
    /// Lấy chuỗi dữ liệu tọa độ viễn trắc để vẽ lại lộ trình bay thực tế (Telemetry Playback)
    /// </summary>
    [HttpGet("{missionId:guid}/telemetry")]
    public async Task<IActionResult> GetTelemetryPlayback(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = TryGetUserId(out var userId) ? userId : (Guid?)null;
            var result = await _service.GetTelemetryPlaybackAsync(
                missionId, currentUserId, IsManager(), cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    /// <summary>
    /// Xem danh sách sự cố đã xảy ra trong chuyến bay (SCR-12 / UC-16)
    /// </summary>
    [HttpGet("{missionId:guid}/incidents")]
    public async Task<IActionResult> GetIncidents(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = TryGetUserId(out var userId) ? userId : (Guid?)null;
            var result = await _service.GetIncidentsAsync(
                missionId, currentUserId, IsManager(), cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    /// <summary>
    /// Bảng phân tích thống kê vận hành tổng thể (SCR-16 / UC-22)
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics(CancellationToken cancellationToken)
    {
        try
        {
            var isManager = IsManager();
            Guid? pilotUserId = isManager ? null : GetRequiredUserId();

            var result = await _service.GetOperationalAnalyticsAsync(pilotUserId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) { return ToErrorResult(ex); }
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }

    private bool IsManager() =>
        User.IsInRole("operation_manager") || User.IsInRole("operations_manager");

    private Guid GetRequiredUserId()
    {
        if (!TryGetUserId(out var userId))
        {
            throw new UnauthorizedAccessException("The authenticated user identifier is missing or invalid.");
        }
        return userId;
    }

    // Chuyển exception nghiệp vụ thành HTTP status rõ nghĩa để client xử lý đúng.
    private IActionResult ToErrorResult(Exception exception) => exception switch
    {
        KeyNotFoundException => NotFound(new { Message = exception.Message }),
        UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { Message = exception.Message }),
        InvalidOperationException => Conflict(new { Message = exception.Message }),
        ArgumentException => BadRequest(new { Message = exception.Message }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Post-flight processing failed." })
    };
}
