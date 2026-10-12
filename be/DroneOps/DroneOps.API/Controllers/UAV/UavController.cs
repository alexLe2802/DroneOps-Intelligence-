using System.Security.Claims;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.UAVs;

[ApiController]
[Route("api/uavs")]
[Authorize(Roles = "Pilot")]
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
        CreateUavRequest request,
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
}