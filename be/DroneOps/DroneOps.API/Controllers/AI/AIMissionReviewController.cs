using DroneOps.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/ai/mission-review")]
[Authorize(Roles = "operation_manager")]
public class AIMissionReviewController : ControllerBase
{
    private readonly IAIMissionReviewService _aiMissionReviewService;

    public AIMissionReviewController(
        IAIMissionReviewService aiMissionReviewService)
    {
        _aiMissionReviewService = aiMissionReviewService;
    }

    /// <summary>
    /// Get AI advice for a mission pending review.
    /// </summary>
    
    [HttpPost("{missionId:guid}")]
    public async Task<IActionResult> ReviewMission(
        Guid missionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aiMissionReviewService.ReviewMissionAsync(
                missionId,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "AI mission review failed.",
                detail = ex.Message
            });
        }
    }
}
