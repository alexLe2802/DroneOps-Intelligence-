using System.Security.Claims;
using DroneOps.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/ai/mission-review")]
[Authorize(Roles = "operation_manager")]
public sealed class AIMissionReviewController : ControllerBase
{
    private readonly IAIMissionReviewService _missionReviewService;

    public AIMissionReviewController(
        IAIMissionReviewService missionReviewService)
    {
        _missionReviewService = missionReviewService;
    }

    [HttpPost("{missionId:guid}")]
    public async Task<IActionResult> ReviewMissionAsync(
    Guid missionId,
    CancellationToken cancellationToken)
    {
        try
        {
            var actorIdText = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(actorIdText, out var actorId))
            {
                return Unauthorized(new
                {
                    message = "Invalid operation manager identity."
                });
            }

            var result = await _missionReviewService.ReviewMissionAsync(
                missionId,
                actorId,
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
            return BadRequest(new
            {
                message = exception.Message
            });
        }
    }
}
