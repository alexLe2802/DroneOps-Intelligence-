
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DroneOps.API.Controllers;

[ApiController]
[Authorize(Roles = "uav_operator,operation_manager")]
[Route("api/users/pilot-update-requests")]
public sealed class PilotUpdateRequestsController : ControllerBase
{
    private readonly IPilotUpdateRequestService _pilotUpdateRequestService;

    public PilotUpdateRequestsController(
        IPilotUpdateRequestService pilotUpdateRequestService)
    {
        _pilotUpdateRequestService = pilotUpdateRequestService;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsManager =>
        User.IsInRole("operation_manager");

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] PilotUpdateRequestFilterRequest filter,
        CancellationToken cancellationToken)
    {
        var result = await _pilotUpdateRequestService.GetAllAsync(
            UserId,
            IsManager,
            filter,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _pilotUpdateRequestService.GetByIdAsync(
            id,
            UserId,
            IsManager,
            cancellationToken);

        if (result is null)
        {
            return NotFound(new
            {
                message = "Pilot update request not found."
            });
        }

        return Ok(result);
    }

    [Authorize(Roles = "uav_operator")]
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Submit(
     [FromForm] PilotUpdateRequestForm form,
     CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _pilotUpdateRequestService.SubmitAsync(
                    UserId,
                    form,
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
        catch (ArgumentException exception)
        {
            return BadRequest(new
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

    [Authorize(Roles = "operation_manager")]
    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result=await _pilotUpdateRequestService.ApproveAsync(id,UserId,cancellationToken);return Ok(result);
        }
        catch(InvalidOperationException exception){return Conflict(new {message=exception.Message});}
    }

    [Authorize(Roles = "operation_manager")]
    [HttpPut("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectPilotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result=await _pilotUpdateRequestService.RejectAsync(id,UserId,request,cancellationToken);return Ok(result);
        }
        catch(InvalidOperationException exception){return Conflict(new {message=exception.Message});}
    }
    private static string GetFileExtension(
    IFormFile file)
    {
        return file.ContentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            _ => Path.GetExtension(file.FileName)
                .ToLowerInvariant()
        };
    }
}
