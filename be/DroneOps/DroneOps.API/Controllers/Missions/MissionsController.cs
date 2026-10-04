using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/missions")]
[Authorize(Roles = "Pilot")]
public sealed class MissionsController : ControllerBase
{
    private readonly IMissionService _missionService;

    public MissionsController(IMissionService missionService)
    {
        _missionService = missionService;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(MissionResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MissionResponse>> CreateAsync(
        [FromBody] CreateMissionRequest request,
        CancellationToken cancellationToken)
    {
        var pilotIdValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(pilotIdValue, out var pilotId))
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });

        var response = await _missionService.CreateAsync(
            pilotId,
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetByIdAsync),
            new { id = response.Id },
            response);
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetByIdAsync(Guid id)
    {
        return Ok();
    }
}