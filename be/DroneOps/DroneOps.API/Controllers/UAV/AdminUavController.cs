using DroneOps.Application.DTOs.Request.UAV;
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

    [HttpPut("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uavService.RejectAsync(id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uavService.ApproveAsync(id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/activate")]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uavService.ActivateAsync(id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _uavService.DeactivateAsync(id, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
