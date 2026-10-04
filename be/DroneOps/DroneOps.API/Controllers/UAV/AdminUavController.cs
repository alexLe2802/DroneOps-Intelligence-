using DroneOps.Application.Interfaces.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.UAVs;

[ApiController]
[Route("api/admin/uavs")]
[Authorize(Roles = "Admin,SystemAdmin")]
public class AdminUavController : ControllerBase
{
    private readonly IUavService _uavService;

    public AdminUavController(
        IUavService uavService)
    {
        _uavService = uavService;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(
        CancellationToken cancellationToken)
    {
        var result =
            await _uavService.GetPendingAsync(
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

    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _uavService.ApproveAsync(
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
        var result =
            await _uavService.RejectAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}/activate")]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _uavService.ActivateAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _uavService.DeactivateAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}