using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Missions;

[ApiController, Route("api/missions/{id:guid}/automation"), Authorize(Roles = "uav_operator")]
public sealed class MissionAutomationController(MissionAutomationService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("/api/missions/automation/due")] public Task<IActionResult> Due(CancellationToken ct) => Run(() => service.DueAsync(Actor, ct));
    [HttpGet] public Task<IActionResult> Read(Guid id, CancellationToken ct) => Run(() => service.ReadAsync(id, Actor, ct));
    [HttpPost("presence")] public Task<IActionResult> Presence(Guid id, CancellationToken ct) => Run(() => service.PresenceAsync(id, Actor, ct));
    [HttpPost("confirm")] public Task<IActionResult> Confirm(Guid id, ConfirmPreparation request, CancellationToken ct) => Run(() => service.ConfirmAsync(id, Actor, request.PlanHash, ct));
    [HttpGet("plan")] public Task<IActionResult> Plan(Guid id, CancellationToken ct) => Run(() => service.PlanAsync(id, Actor, ct));
    [HttpPost("cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => await Run(async () => { await service.CancelAsync(id, Actor, ct); return new { cancelled = true }; });
    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (KeyNotFoundException error) { return NotFound(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
    public sealed record ConfirmPreparation(string PlanHash);
}
