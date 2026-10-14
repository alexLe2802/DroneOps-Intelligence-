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

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}