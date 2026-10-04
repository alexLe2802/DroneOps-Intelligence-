using DroneOps.Application.DTOs.Request.Geofences;
using DroneOps.Application.DTOs.Response.Geofences;
using DroneOps.Application.Interfaces.Geofences;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/geofences")]
[Authorize]
public class GeofencesController : ControllerBase
{
    private readonly IGeofenceService _geofenceService;

    public GeofencesController(
        IGeofenceService geofenceService)
    {
        _geofenceService = geofenceService;
    }

    [HttpGet]
    [Authorize(Roles = "SystemAdmin,Admin,Pilot")]
    public async Task<ActionResult<List<GeofenceResponse>>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var result =
            await _geofenceService.GetAllAsync(
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SystemAdmin,Admin,Pilot")]
    public async Task<ActionResult<GeofenceResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _geofenceService.GetByIdAsync(
                    id,
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

    [HttpPost]
    [Authorize(Roles = "SystemAdmin,Admin")]
    public async Task<ActionResult<GeofenceResponse>> CreateAsync(
        [FromBody] CreateGeofenceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _geofenceService.CreateAsync(
                    request,
                    cancellationToken);

            return StatusCode(
  StatusCodes.Status201Created,
  result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new
            {
                message = exception.Message
            });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SystemAdmin,Admin")]
    public async Task<ActionResult<GeofenceResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateGeofenceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _geofenceService.UpdateAsync(
                    id,
                    request,
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
        catch (InvalidOperationException exception)
        {
            return Conflict(new
            {
                message = exception.Message
            });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SystemAdmin,Admin")]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _geofenceService.DeleteAsync(
                id,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
    }
}