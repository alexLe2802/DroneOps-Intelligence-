using System.Security.Claims;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.UAVs;

[ApiController]
[Route("api/uavs")]
[Authorize(Roles = "uav_operator,operation_manager,operations_manager")]
public class UavController : ControllerBase
{
    private readonly IUavService _uavService;

    public UavController(
        IUavService uavService)
    {
        _uavService = uavService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUavRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!
                .Value);

        var result =
            await _uavService.CreateAsync(
                userId,
                request,
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("myUAV")]
    public async Task<IActionResult> GetMyUavs(
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!
                .Value);

        var result =
            await _uavService.GetMyUavsAsync(
                userId,
                cancellationToken);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUavRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!
                .Value);

        var result = await _uavService.UpdateAsync(
            userId,
            id,
            request,
            cancellationToken);

        if (!result)
        {
            return NotFound(new { message = "UAV not found or unauthorized." });
        }

        return Ok(new { message = "UAV updated successfully." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!
                .Value);

        var result = await _uavService.DeleteAsync(
            userId,
            id,
            cancellationToken);

        if (!result)
        {
            return NotFound(new { message = "UAV not found or unauthorized." });
        }

        return Ok(new { message = "UAV deleted successfully." });
    }
}