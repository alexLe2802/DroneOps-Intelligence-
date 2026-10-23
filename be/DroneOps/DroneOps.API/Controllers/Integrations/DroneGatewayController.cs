using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace DroneOps.API.Controllers.Integrations;

[ApiController,Route("api/drone-gateway"),AllowAnonymous,RequestSizeLimit(16384)]
public sealed class DroneGatewayController(IDroneConnectionService service) : ControllerBase
{
    // These routes use a separate, revocable gateway credential, not pilot JWTs.
    private string Token => Request.Headers.Authorization.ToString() is var h && h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : "";
    [HttpPost("pair"),EnableRateLimiting("gateway-pairing")]
    public async Task<IActionResult> Pair(PairDroneGateway request,CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        return Ok(await service.PairAsync(request,ct));
    }
    [HttpPost("{id:guid}/heartbeat"),EnableRateLimiting("gateway-ingest")]
    public async Task<IActionResult> Heartbeat(Guid id,CancellationToken ct)
    { await service.HeartbeatAsync(id,Token,Request.Headers["X-Drone-Identity"].ToString(),ct);return NoContent(); }
    [HttpPost("{id:guid}/samples"),EnableRateLimiting("gateway-ingest")]
    public async Task<IActionResult> Sample(Guid id,[FromHeader(Name="Idempotency-Key")]string? key,IngestTelemetrySampleRequest request,CancellationToken ct)
    { var result=await service.SampleAsync(id,Token,Request.Headers["X-Drone-Identity"].ToString(),key??"",request,ct);return StatusCode(result.IsDuplicate?200:201,result); }
}
