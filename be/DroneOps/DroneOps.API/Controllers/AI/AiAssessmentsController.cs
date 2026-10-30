using System.Security.Claims;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.AI;
using DroneOps.Application.Interfaces.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.AI;

[ApiController]
[Route("api/ai/assessments")]
[Authorize(Roles = "Pilot,uav_operator,operation_manager,operations_manager")]
public sealed class AiAssessmentsController(IAiAssessmentService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AiAssessmentResponse>> Create(CreateAiAssessmentRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.CreateAsync(request, UserId(), IsManager(), cancellationToken)); }
        catch (Exception exception) { return Error(exception); }
    }

    [HttpGet("mission/{missionRef}")]
    public async Task<ActionResult<List<AiAssessmentResponse>>> GetByMission(string missionRef, CancellationToken cancellationToken)
    {
        try { return Ok(await service.GetByMissionAsync(missionRef, UserId(), IsManager(), cancellationToken)); }
        catch (Exception exception) { return Error(exception); }
    }

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("Authenticated user identifier is missing.");
    private bool IsManager() => User.IsInRole("operation_manager") || User.IsInRole("operations_manager");
    private ActionResult Error(Exception exception) => exception switch
    {
        ArgumentException => BadRequest(new { message = exception.Message }),
        KeyNotFoundException => NotFound(new { message = exception.Message }),
        UnauthorizedAccessException => StatusCode(403, new { message = exception.Message }),
        _ => StatusCode(500, new { message = "Assessment generation failed." })
    };
}
