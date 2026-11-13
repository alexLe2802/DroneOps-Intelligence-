using System.Security.Claims;
using System.Text.Json;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace DroneOps.API.Controllers.Incidents;
[ApiController, Route("api/incidents/rules"), Authorize(Roles = "operation_manager,operations_manager")]
public sealed class MonitoringRulesController(AppDbContext db, IOptions<TelemetryMonitoringOptions> defaults) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Read(CancellationToken ct)
    {
        var revisions = await db.MonitoringRuleRevisions.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(20).ToListAsync(ct);
        return Ok(new { settings = revisions.Count == 0 ? defaults.Value : JsonSerializer.Deserialize<TelemetryMonitoringOptions>(revisions[0].SettingsJson), revisions });
    }
    [HttpPut] public async Task<IActionResult> Update(RuleChange request, CancellationToken ct)
    {
        var settings = request.Settings;
        if (settings.LowBatteryCriticalPercent is < 0 || settings.LowBatteryCriticalPercent >= settings.LowBatteryPercent || settings.LowBatteryPercent is < 1 or > 95 || settings.LowBatteryRecoveryMarginPercent < 0
            || settings.LowBatteryPercent + settings.LowBatteryRecoveryMarginPercent > 100
            || settings.CommunicationLossSeconds is < 1 or > 120 || settings.ScanIntervalSeconds is < 1 or > 10
            || settings.ScanIntervalSeconds > settings.CommunicationLossSeconds || settings.StaleAfterSeconds < 1
            || settings.OfflineAfterSeconds <= settings.StaleAfterSeconds
            || settings.MinimumAltitudeMslMeters.HasValue != settings.MaximumAltitudeMslMeters.HasValue
            || settings.MinimumAltitudeMslMeters > settings.MaximumAltitudeMslMeters)
            return BadRequest(new { message = "Invalid detector thresholds or recovery/freshness intervals." });
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Forbid();
        return await DroneOps.Persistence.RealtimeWrite.RunAsync<IActionResult>(db, async () =>
        {
            var current = await db.MonitoringRuleRevisions.OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
            if (current?.Id != request.ExpectedRevision) return Conflict(new { message = "Rule configuration changed. Reload before saving." });
            var revision = new MonitoringRuleRevision { Id = Guid.NewGuid(), ChangedBy = actor, CreatedAt = DateTimeOffset.UtcNow,
                SettingsJson = JsonSerializer.Serialize(settings) };
            db.MonitoringRuleRevisions.Add(revision); await db.SaveChangesAsync(ct);
            return Ok(new { revision.Id, revision.ChangedBy, revision.CreatedAt, settings });
        }, ct);
    }
    public sealed record RuleChange(TelemetryMonitoringOptions Settings, Guid? ExpectedRevision);
}
