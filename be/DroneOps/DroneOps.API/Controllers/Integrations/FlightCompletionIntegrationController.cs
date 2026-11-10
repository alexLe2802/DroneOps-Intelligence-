using DroneOps.API.Filters;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Integrations;

[ApiController]
[Route("api/integrations/flight-completions")]
[IntegrationAuthorize]
public sealed class FlightCompletionIntegrationController : ControllerBase
{
    private readonly IPostFlightService _service;

    public FlightCompletionIntegrationController(IPostFlightService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> RecordCompletion(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] RecordFlightCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("IntegrationSettings:AllowLegacyCompletion"))
            return Conflict(new { message = "Completion is reconciled from the bound gateway flight evidence; legacy completion is disabled." });
        var allowed = configuration.GetSection("IntegrationSettings:Sources").GetChildren().Any(source =>
            source["SourceId"] == request.SourceId && Guid.TryParse(source["UavId"], out var uav) && uav == request.UavId);
        if (!allowed) return StatusCode(403, new { message = "Completion source is not configured for this UAV." });
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { message = "Header 'Idempotency-Key' is required." });
        try
        {
            var report = await _service.RecordTrustedCompletionAsync(
                idempotencyKey, request, cancellationToken);
            return Accepted(new
            {
                success = true,
                missionId = request.MissionId,
                reportId = report.ReportId,
                reportStatus = report.GenerationStatus,
                revision = report.Revision
            });
        }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }
}
