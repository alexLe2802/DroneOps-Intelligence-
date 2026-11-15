using System.Security.Claims;
using DroneOps.Application.Services.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace DroneOps.API.Controllers.UAVs;

[ApiController, Route("api/uavs/{id:guid}/reviews"), Authorize(Roles = "operation_manager,uav_operator")]
public sealed class UavReviewsController(UavReviewWorkflow reviews) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        try { return Ok(await reviews.ReadAsync(id, actor, User.IsInRole("operation_manager"), ct)); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (UnauthorizedAccessException exception) { return StatusCode(403, new { message = exception.Message }); }
    }
}
