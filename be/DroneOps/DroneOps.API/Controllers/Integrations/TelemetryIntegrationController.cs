using DroneOps.API.Filters;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Integrations;

[ApiController]
[Route("api/integrations/telemetry")]
[IntegrationAuthorize]
public class TelemetryIntegrationController : ControllerBase
{
    private readonly ITelemetryIngestionService _telemetryService;
    private readonly ILogger<TelemetryIntegrationController> _logger;

    public TelemetryIntegrationController(
        ITelemetryIngestionService telemetryService,
        ILogger<TelemetryIntegrationController> logger)
    {
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <summary>
    /// Nhận và lưu trữ mẫu telemetry MAVLink từ drone hoặc simulator (D-05)
    /// </summary>
    /// <param name="idempotencyKey">Khóa chống trùng lặp định dạng {sourceId}:{messageSequence}:{sourceTimestamp}</param>
    /// <param name="request">Payload thông số viễn trắc chi tiết của drone</param>
    /// <param name="cancellationToken">Token hủy tác vụ</param>
    [HttpPost("samples")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestTelemetrySample(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] IngestTelemetrySampleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return BadRequest(new
                {
                    message = "Header 'Idempotency-Key' is required to prevent duplicate processing."
                });
            }

            var result = await _telemetryService.IngestSampleAsync(
                idempotencyKey,
                request,
                cancellationToken);

            var payload = new
            {
                success = result.Accepted,
                duplicate = result.IsDuplicate,
                lateArrival = result.IsLateArrival,
                usedForLatestProjection = result.UsedForLatestProjection,
                message = result.Message,
                recordId = result.RecordId,
                raisedIncidentId = result.RaisedIncidentId,
                sourceId = request.SourceId,
                uavId = request.UavId,
                receivedAt = DateTimeOffset.UtcNow
            };

            // Replay idempotent trả 200; gói mới trả 201.
            return result.IsDuplicate
                ? Ok(payload)
                : StatusCode(StatusCodes.Status201Created, payload);
        }
        catch (KeyNotFoundException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during telemetry ingestion.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Telemetry ingestion failed." });
        }
    }
}