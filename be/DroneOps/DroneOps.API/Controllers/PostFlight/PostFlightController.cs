using System.Security.Claims;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.PostFlight;

[ApiController]
[Route("api/flight-history")]
[Authorize]
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
            var isPilotOnly = User.IsInRole("Pilot") && !User.IsInRole("Admin") && !User.IsInRole("OperationsManager");
            Guid? pilotUserId = isPilotOnly && TryGetUserId(out var userId) ? userId : null;

            var result = await _service.GetFlightHistoryAsync(request, pilotUserId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Chốt hoàn tất chuyến bay và tự động lập Post-Flight Report (UC-17)
    /// </summary>
    [HttpPost("{missionId:guid}/finalize")]
    public async Task<IActionResult> FinalizeFlight(Guid missionId, [FromBody] FinalizeFlightRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.FinalizeAndGenerateReportAsync(missionId, request, cancellationToken);
            return Ok(new
            {
                Success = true,
                Message = "Chuyến bay đã được chốt hoàn tất và tạo báo cáo phân tích thành công.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Xem chi tiết báo cáo phân tích sau bay (SCR-14 / UC-18)
    /// </summary>
    [HttpGet("{missionId:guid}/report")]
    public async Task<IActionResult> GetReport(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetReportByMissionIdAsync(missionId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy chuỗi dữ liệu tọa độ viễn trắc để vẽ lại lộ trình bay thực tế (Telemetry Playback)
    /// </summary>
    [HttpGet("{missionId:guid}/telemetry")]
    public async Task<IActionResult> GetTelemetryPlayback(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetTelemetryPlaybackAsync(missionId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Xem danh sách sự cố đã xảy ra trong chuyến bay (SCR-12 / UC-16)
    /// </summary>
    [HttpGet("{missionId:guid}/incidents")]
    public async Task<IActionResult> GetIncidents(Guid missionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetIncidentsAsync(missionId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Bảng phân tích thống kê vận hành tổng thể (SCR-16 / UC-22)
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics(CancellationToken cancellationToken)
    {
        try
        {
            var isPilotOnly = User.IsInRole("Pilot") && !User.IsInRole("Admin") && !User.IsInRole("OperationsManager");
            Guid? pilotUserId = isPilotOnly && TryGetUserId(out var userId) ? userId : null;

            var result = await _service.GetOperationalAnalyticsAsync(pilotUserId, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}