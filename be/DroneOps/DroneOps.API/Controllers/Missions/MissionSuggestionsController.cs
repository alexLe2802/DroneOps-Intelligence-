using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Missions;

[ApiController]
[Route("api/mission-suggestions")]
[Authorize(Roles = "Pilot")]
public sealed class MissionSuggestionsController
    : ControllerBase
{
    private readonly IMissionSuggestionService _service;

    public MissionSuggestionsController(
        IMissionSuggestionService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<MissionSuggestionResponse>>
        GenerateAsync(
            [FromBody]
            GenerateMissionSuggestionRequest request,
            CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        try
        {
            var result =
                await _service.GenerateAsync(
                    userId,
                    request,
                    cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }
    }

    [HttpGet("mine")]
    public async Task<
        ActionResult<List<MissionSuggestionResponse>>>
        GetMineAsync(
            CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result =
                await _service.GetMineAsync(
                    userId,
                    cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MissionSuggestionResponse>>
        GetMineByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result =
                await _service.GetMineByIdAsync(
                    id,
                    userId,
                    cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
    }

    private bool TryGetCurrentUserId(
        out Guid userId)
    {
        var value =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        return Guid.TryParse(
            value,
            out userId);
    }
}