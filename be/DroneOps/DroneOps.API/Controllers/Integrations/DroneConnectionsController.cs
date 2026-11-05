using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace DroneOps.API.Controllers.Integrations;

[ApiController,Route("api/drone-connections"),Authorize(Roles="uav_operator")]
public sealed class DroneConnectionsController(IDroneConnectionService service, TrustedGatewayStore? trustedGateways = null) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id) ? id : throw new System.Security.Authentication.AuthenticationException("Invalid pilot identity.");
    [HttpDelete("aircraft/{uavId:guid}/gateway")]
    public async Task<IActionResult> ForgetGateway(Guid uavId, CancellationToken ct)
    {
        if (trustedGateways is null) return StatusCode(503);
        await trustedGateways.RevokeAsync(uavId, Actor, ct);
        return NoContent();
    }
    [HttpGet("aircraft")]
    public async Task<IActionResult> Bindings(CancellationToken ct)
    {
        var bindings = await service.BindingsAsync(Actor, ct);
        var registered = trustedGateways is null ? [] : await trustedGateways.RegisteredUavsAsync(Actor, ct);
        return Ok(bindings.Select(binding => binding with { GatewayRegistered = registered.Contains(binding.UavId) }));
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(Actor,ct));
    [HttpPost,EnableRateLimiting("gateway-pairing")]
    public async Task<IActionResult> Create(CreateDroneConnection request,CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        return StatusCode(201,await service.CreateAsync(Actor,request,ct));
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id,CancellationToken ct) => await service.RevokeAsync(id,Actor,ct) ? NoContent() : NotFound(new {message="Connection not found."});
}
