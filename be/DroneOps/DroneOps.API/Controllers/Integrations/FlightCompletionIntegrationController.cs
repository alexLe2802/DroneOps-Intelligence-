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
