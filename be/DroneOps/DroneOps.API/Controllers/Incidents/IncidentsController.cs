using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Incidents;
using DroneOps.Application.DTOs.Response.Incidents;
using DroneOps.Application.Interfaces.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Incidents;

[ApiController]
[Route("api/incidents")]
[Authorize(Roles = "Pilot,operation_manager,uav_operator,operations_manager")]
public class IncidentsController : ControllerBase
{
    private readonly IIncidentService _incidentService;

    public IncidentsController(IIncidentService incidentService)
    {
        _incidentService = incidentService;
    }

    /// <summary>
    /// Lấy danh sách sự cố có bộ lọc đa tiêu chí và phân trang (SCR-11 / UC-15 / KAN-36)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedIncidentsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncidents([FromQuery] GetIncidentsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var isManager = User.IsInRole("operation_manager") || User.IsInRole("operations_manager");
            Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

            var result = await _incidentService.GetIncidentsAsync(request, currentUserId, isManager, cancellationToken);
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

    /// <summary>
    /// Xem chi tiết sự cố kèm bằng chứng viễn trắc đóng băng và lịch sử xử lý (SCR-12 / UC-16 / KAN-36)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IncidentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidentById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var isManager = User.IsInRole("operation_manager") || User.IsInRole("operations_manager");
            Guid? currentUserId = TryGetUserId(out var userId) ? userId : null;

            var result = await _incidentService.GetIncidentByIdAsync(id, currentUserId, isManager, cancellationToken);
            if (result == null)
            {
                return NotFound(new { message = $"Incident with ID {id} was not found." });
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

    /// <summary>Người vận hành xác nhận đã tiếp nhận sự cố.</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(typeof(IncidentDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Acknowledge(
        Guid id, [FromBody] AcknowledgeIncidentRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteMutationAsync((userId, isManager) =>
            _incidentService.AcknowledgeAsync(id, userId, isManager, request.Notes, cancellationToken));
    }

    /// <summary>Người vận hành đóng sự cố và ghi lại kết quả xử lý.</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(typeof(IncidentDetailResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(
        Guid id, [FromBody] ResolveIncidentRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteMutationAsync((userId, isManager) =>
            _incidentService.ResolveAsync(id, userId, isManager, request.ResolutionNotes, cancellationToken));
    }

    /// <summary>Lấy lịch sử chuyển trạng thái append-only của sự cố.</summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(List<IncidentTransitionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var isManager = IsManager();
            Guid? userId = TryGetUserId(out var parsed) ? parsed : null;
            return Ok(await _incidentService.GetHistoryAsync(id, userId, isManager, cancellationToken));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    /// <summary>Lấy các cảnh báo chưa đọc đã được lưu bền vững trong DB.</summary>
    [HttpGet("alerts/unread")]
    [ProducesResponseType(typeof(List<IncidentAlertResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadAlerts(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _incidentService.GetUnreadAlertsAsync(userId, cancellationToken));
    }

    /// <summary>Đánh dấu một bản giao cảnh báo của chính người dùng là đã đọc.</summary>
    [HttpPost("alerts/{alertId:guid}/read")]
    public async Task<IActionResult> MarkAlertRead(Guid alertId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            await _incidentService.MarkAlertReadAsync(alertId, userId, cancellationToken);
            return Ok(new { message = "Alert marked as read." });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    private async Task<IActionResult> ExecuteMutationAsync(
        Func<Guid, bool, Task<IncidentDetailResponse>> action)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try { return Ok(await action(userId, IsManager())); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private bool IsManager() =>
        User.IsInRole("operation_manager") || User.IsInRole("operations_manager");

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
