using DroneOps.Application.DTOs.Request.UAV;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Request.Uavs;
using DroneOps.Application.Interfaces.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.UAVs;

[ApiController]
[Route("api/admin/uavs")]
[Authorize(Roles = "operation_manager")]
public class AdminUavController : ControllerBase
{
    private readonly IUavService _uavService;

    public AdminUavController(
        IUavService uavService)
    {
        _uavService = uavService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
    [FromQuery] GetAllUavsRequest request,
    CancellationToken cancellationToken = default)
    {
        var result =
            await _uavService.GetAllAsync(
                request,
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(
     [FromQuery] string? keyword,
     [FromQuery] int page = 1,
     [FromQuery] int pageSize = 10,
     CancellationToken cancellationToken = default)
    {
        var result =
            await _uavService.GetPendingAsync(
                keyword,
                page,
                pageSize,
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _uavService.GetDetailAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}/{operation:regex(^(approve|reject|activate|deactivate)$)}")]
    public async Task<IActionResult> Decide(Guid id, string operation,
        [FromBody] DroneOps.Application.Services.UAVs.UavReviewDecision request,
        [FromServices] DroneOps.Application.Services.UAVs.UavReviewWorkflow reviews,
        CancellationToken cancellationToken)
    {
        var actorValue = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(actorValue, out var actor)) return Unauthorized();
        try
        {
            await reviews.DecideAsync(id, actor, operation, request, cancellationToken);
            return Ok(await _uavService.GetDetailAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPost("{id:guid}/image/reject")]
    public async Task<IActionResult> RejectImage(
        Guid id,
        [FromBody] RejectUavImageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uavService.RejectUavImageAsync(id, request.Reason, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "UAV changed concurrently. Reload before reviewing its image." }); }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
