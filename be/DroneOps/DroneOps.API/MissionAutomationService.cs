using System.Data;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using DroneOps.Application.Services.Missions;

namespace DroneOps.API;

public sealed record FlightPreparation(Guid MissionId, string PlanHash, DateTimeOffset ServerNow,
    DateTimeOffset ScheduledAt, bool ConfirmationOpen, bool DroneConnected, bool OperatorOnline,
    bool ReadinessConfirmed, bool DispatchEnabled, string[] BlockingReasons,
    DateTimeOffset EndsAt, bool GatewayConnected, bool PreflightReady, bool ConfirmationRecorded);

public sealed class MissionAutomationService(AppDbContext db, IMissionValidationService validation)
{
    public async Task<object> DueAsync(Guid actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var missions = await db.Missions.AsNoTracking().Where(m => m.CreatedBy == actor && m.AutomaticFlightRequested
            && m.Status == "Approved" && m.EndTime > now && m.StartTime <= now.AddMinutes(5))
            .OrderBy(m => m.StartTime).Select(m => new { m.Id, m.Name, m.StartTime, m.EndTime }).ToListAsync(ct);
        return new { serverNow = now, missions };
    }

    public async Task CancelAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Mission\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            await OwnedMission(id, actor, ct);
            var row = await db.MissionAutomationStates.SingleOrDefaultAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
            if (row != null)
            {
                row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null; row.LastPresenceAt = null;
                if (row.ExecutionJson != null)
                {
                    var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
                    var execution = System.Text.Json.JsonSerializer.Deserialize<FlightControlState>(row.ExecutionJson, json);
                    if (execution != null) { execution.Cancel("Pilot withdrew flight confirmation."); execution.PilotAt = null;
                        row.ExecutionJson = System.Text.Json.JsonSerializer.Serialize(execution, json); }
                }
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        });
    }

    public static bool InConfirmationWindow(DateTimeOffset now, DateTimeOffset start, DateTimeOffset end) =>
        end > start && now >= start.AddMinutes(-5) && now < end;

    private async Task<Mission> OwnedMission(Guid id, Guid actor, CancellationToken ct, bool requireApproved = true)
    {
        var mission = await db.Missions.AsNoTracking().Include(m => m.Waypoints)
            .SingleOrDefaultAsync(m => m.Id == id && m.CreatedBy == actor, ct)
            ?? throw new KeyNotFoundException("Mission not found.");
        if (requireApproved && (!mission.AutomaticFlightRequested || mission.Status != "Approved" || mission.StartTime is null))
            throw new InvalidOperationException("Automatic preparation requires an approved, opted-in mission with a schedule.");
        return mission;
    }

    private Task<DroneConnection?> Connected(Mission mission, DateTimeOffset now, CancellationToken ct, bool requireSample = true) =>
        db.DroneConnections.AsNoTracking().Where(c => c.MissionId == mission.Id && c.UavId == mission.UAVId
            && c.OwnerId == mission.CreatedBy && c.RevokedAt == null && c.ExpiresAt > now && c.PairedAt != null
            && c.LastGatewayAt >= now.AddSeconds(-30) && (!requireSample || c.LastSampleAt >= now.AddSeconds(-15))
            && db.UAVs.Any(u => u.Id == c.UavId && u.Status == "Active" && u.Pilot.UserId == mission.CreatedBy)
            && db.DroneHardwareIdentities.Any(h => h.UavId == c.UavId && h.Identity == c.HardwareIdentity))
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<FlightPreparation> ReadAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var mission = await OwnedMission(id, actor, ct);
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(mission.EndTime ?? mission.StartTime.Value, DateTimeKind.Utc));
        var windowOpen = InConfirmationWindow(now, start, end);
        var hash = MissionValidationService.Hash(mission);
        var connection = await Connected(mission, now, ct);
        var gatewayConnected = connection != null || await Connected(mission, now, ct, requireSample: false) != null;
        var state = await db.MissionAutomationStates.AsNoTracking().SingleOrDefaultAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
        var preflight = await db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == id && r.Stage == "Preflight")
            .OrderByDescending(r => r.CheckedAt).FirstOrDefaultAsync(ct);
        var freshPreflight = preflight is { IsValid: true, ResultStatus: "Pass" } && preflight.PlanHash == hash
            && preflight.CheckedAt >= now.AddMinutes(-1) && preflight.CheckedAt <= now;
        var online = state?.LastPresenceAt >= now.AddSeconds(-30);
        var recorded = windowOpen && online && state?.ConfirmedPlanHash == hash && state.ConfirmedAt >= start.AddMinutes(-5)
            && state.ConfirmedAt < end && connection != null && state.ConnectionId == connection.Id;
        var confirmed = recorded && freshPreflight;
        var reasons = new List<string>();
        if (!windowOpen) reasons.Add(now >= end ? "The mission has ended. Confirmation is closed." : "Confirmation opens five minutes before the scheduled start.");
        if (connection == null) reasons.Add("Fresh telemetry from this mission's bound flight controller is required.");
        if (!online) reasons.Add("Keep an authenticated, visible browser tab online.");
        if (!freshPreflight) reasons.Add("A fresh, passing preflight check for this exact plan is required.");
        if (!recorded) reasons.Add("The pilot has not confirmed this exact plan and drone connection.");
        // This endpoint reports preparation consent; execution is a separate,
        // explicitly gated action in the same automation API.
        reasons.Add("Preparation confirmation does not arm or launch the drone. Use the pilot controls in Live Monitoring.");
        return new(id, hash, now, start, windowOpen, connection != null,
            online, confirmed, false, reasons.ToArray(), end, gatewayConnected, freshPreflight, recorded);
    }

    public async Task<FlightPreparation> PresenceAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var mission = await OwnedMission(id, actor, ct);
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(mission.EndTime ?? mission.StartTime.Value, DateTimeKind.Utc));
        if (!InConfirmationWindow(now, start, end)) return await ReadAsync(id, actor, ct);
        var invalidConnection = await Connected(mission, now, ct) is null;
        var stalePresence = now.AddSeconds(-30);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."MissionAutomationState" ("MissionId", "OwnerId", "LastPresenceAt")
            VALUES ({id}, {actor}, {now}) ON CONFLICT ("MissionId")
            DO UPDATE SET "LastPresenceAt" = EXCLUDED."LastPresenceAt",
                "ConfirmedAt" = CASE WHEN {invalidConnection} OR "MissionAutomationState"."LastPresenceAt" < {stalePresence} THEN NULL ELSE "MissionAutomationState"."ConfirmedAt" END,
                "ConfirmedPlanHash" = CASE WHEN {invalidConnection} OR "MissionAutomationState"."LastPresenceAt" < {stalePresence} THEN NULL ELSE "MissionAutomationState"."ConfirmedPlanHash" END,
                "ConnectionId" = CASE WHEN {invalidConnection} OR "MissionAutomationState"."LastPresenceAt" < {stalePresence} THEN NULL ELSE "MissionAutomationState"."ConnectionId" END
            WHERE "MissionAutomationState"."OwnerId" = EXCLUDED."OwnerId"
            """, ct);
        return await ReadAsync(id, actor, ct);
    }

    public async Task<FlightPreparation> ConfirmAsync(Guid id, Guid actor, string expectedHash, CancellationToken ct)
    {
        var before = await ReadAsync(id, actor, ct);
        if (expectedHash != before.PlanHash || !before.ConfirmationOpen || !before.OperatorOnline || !before.DroneConnected)
            throw new InvalidOperationException("Refresh the plan. Confirmation requires T-5 through mission end, an online operator and fresh telemetry.");
        var result = await validation.RevalidateAsync(id, actor, false, "Preflight", ct);
        if (!result.IsValid || result.Error || result.Stale || result.Incomplete)
            throw new InvalidOperationException("Preflight did not pass: " + result.Message);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Mission\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var mission = await OwnedMission(id, actor, ct);
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(mission.EndTime ?? mission.StartTime.Value, DateTimeKind.Utc));
            var hash = MissionValidationService.Hash(mission);
            var connection = await Connected(mission, now, ct);
            var runMatches = await db.MissionValidationRuns.AnyAsync(r => r.Id == result.Id && r.MissionId == id && r.PlanHash == hash && r.IsValid, ct);
            var state = await db.MissionAutomationStates.SingleAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
            // Presence uses an SQL upsert. Never validate a previously tracked
            // snapshot after cancellation/presence changed in this scope.
            await db.Entry(state).ReloadAsync(ct);
            if (hash != expectedHash || !runMatches || connection == null || !InConfirmationWindow(now, start, end)
                || state.LastPresenceAt is null || state.LastPresenceAt < now.AddSeconds(-30))
                throw new InvalidOperationException("The plan, connection or operator presence changed. Review and confirm again.");
            state.ConfirmedAt = now;
            state.ConfirmedPlanHash = hash;
            state.ConnectionId = connection.Id;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        return await ReadAsync(id, actor, ct);
    }

    public async Task<object> PlanAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var mission = await OwnedMission(id, actor, ct, requireApproved: false);
        return new
        {
            schemaVersion = 1, missionId = mission.Id, uavId = mission.UAVId,
            planHash = MissionValidationService.Hash(mission), dispatchEnabled = false,
            scheduledStartUtc = mission.StartTime, scheduledEndUtc = mission.EndTime,
            altitudeReference = mission.AltitudeReference, averageSpeedMetersPerSecond = mission.AverageSpeedMetersPerSecond,
            automaticFlightLimits = mission.AutomaticFlightLimits,
            recoveryPolicy = mission.AutomaticFlightRequested ? AutomaticFailsafePolicy.Contract(mission.MaxSpeedMetersPerSecond ?? 0) : null,
            maxSpeedMetersPerSecond = mission.MaxSpeedMetersPerSecond,
            supportedFirmwareTargets = new[] { "ArduPilot", "PX4" },
            compilationPreviews = new[] { "ArduPilot", "PX4" }.Select(firmware =>
                MissionCommandCompiler.Compile(mission, new MissionCompileOptions(firmware))).ToArray(),
            waypoints = mission.Waypoints.OrderBy(w => w.SequenceOrder).Select(w => new
            {
                sequenceOrder = w.SequenceOrder, latitudeDegrees = w.Latitude, longitudeDegrees = w.Longitude,
                latitudeE7 = (int)decimal.Round(w.Latitude * 10000000m, 0, MidpointRounding.AwayFromZero),
                longitudeE7 = (int)decimal.Round(w.Longitude * 10000000m, 0, MidpointRounding.AwayFromZero),
                altitudeMeters = w.Altitude, action = w.ActionType
            }),
            requiredBeforeDispatch = new[] { "Detected firmware/version/vehicle type and capabilities", "Explicit altitude frame and home/terrain data",
                "Per-action yaw/hold parameters", "Failsafe/RTL configuration", "Upload ACK and read-back plan hash", "Fresh pre-arm checks and command acknowledgements" }
        };
    }
}
