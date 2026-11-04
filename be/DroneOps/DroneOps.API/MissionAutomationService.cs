using System.Data;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API;

public sealed record FlightPreparation(Guid MissionId, string PlanHash, DateTimeOffset ServerNow,
    DateTimeOffset ScheduledAt, bool ConfirmationOpen, bool DroneConnected, bool OperatorOnline,
    bool ReadinessConfirmed, bool DispatchEnabled, string[] BlockingReasons);

public sealed class MissionAutomationService(AppDbContext db, IMissionValidationService validation)
{
    public async Task<object> DueAsync(Guid actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var missions = await db.Missions.AsNoTracking().Where(m => m.CreatedBy == actor && m.AutomaticFlightRequested
            && m.Status == "Approved" && m.StartTime > now && m.StartTime <= now.AddMinutes(5))
            .OrderBy(m => m.StartTime).Select(m => new { m.Id, m.Name, m.StartTime }).Take(20).ToListAsync(ct);
        return new { serverNow = now, missions };
    }

    public async Task CancelAsync(Guid id, Guid actor, CancellationToken ct)
    {
        await OwnedMission(id, actor, ct);
        await db.MissionAutomationStates.Where(s => s.MissionId == id && s.OwnerId == actor).ExecuteUpdateAsync(
            updates => updates.SetProperty(s => s.ConfirmedAt, (DateTimeOffset?)null)
                .SetProperty(s => s.ConfirmedPlanHash, (string?)null).SetProperty(s => s.ConnectionId, (Guid?)null)
                .SetProperty(s => s.LastPresenceAt, (DateTimeOffset?)null), ct);
    }

    public static bool InConfirmationWindow(DateTimeOffset now, DateTimeOffset start) =>
        now >= start.AddMinutes(-5) && now < start;

    private async Task<Mission> OwnedMission(Guid id, Guid actor, CancellationToken ct, bool requireApproved = true)
    {
        var mission = await db.Missions.AsNoTracking().Include(m => m.Waypoints)
            .SingleOrDefaultAsync(m => m.Id == id && m.CreatedBy == actor, ct)
            ?? throw new KeyNotFoundException("Mission not found.");
        if (requireApproved && (!mission.AutomaticFlightRequested || mission.Status != "Approved" || mission.StartTime is null))
            throw new InvalidOperationException("Automatic preparation requires an approved, opted-in mission with a schedule.");
        return mission;
    }

    private Task<DroneConnection?> Connected(Mission mission, DateTimeOffset now, CancellationToken ct) =>
        db.DroneConnections.AsNoTracking().Where(c => c.MissionId == mission.Id && c.UavId == mission.UAVId
            && c.OwnerId == mission.CreatedBy && c.RevokedAt == null && c.ExpiresAt > now && c.PairedAt != null
            && c.LastGatewayAt >= now.AddSeconds(-30) && c.LastSampleAt >= now.AddSeconds(-15)
            && db.UAVs.Any(u => u.Id == c.UavId && u.Status == "Active" && u.Pilot.UserId == mission.CreatedBy)
            && db.DroneHardwareIdentities.Any(h => h.UavId == c.UavId && h.Identity == c.HardwareIdentity))
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<FlightPreparation> ReadAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var mission = await OwnedMission(id, actor, ct);
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
        var hash = MissionValidationService.Hash(mission);
        var connection = await Connected(mission, now, ct);
        var state = await db.MissionAutomationStates.AsNoTracking().SingleOrDefaultAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
        var preflight = await db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == id && r.Stage == "Preflight")
            .OrderByDescending(r => r.CheckedAt).FirstOrDefaultAsync(ct);
        var freshPreflight = preflight is { IsValid: true, ResultStatus: "Pass" } && preflight.PlanHash == hash
            && preflight.CheckedAt >= now.AddMinutes(-1) && preflight.CheckedAt <= now;
        var online = state?.LastPresenceAt >= now.AddSeconds(-30);
        var confirmed = online && freshPreflight && state?.ConfirmedPlanHash == hash && state.ConfirmedAt >= start.AddMinutes(-5)
            && state.ConfirmedAt < start && connection != null && state.ConnectionId == connection.Id;
        var reasons = new List<string>();
        if (!InConfirmationWindow(now, start)) reasons.Add(now >= start ? "The confirmation window has closed. Reschedule and confirm again." : "Confirmation opens five minutes before the scheduled start.");
        if (connection == null) reasons.Add("Fresh telemetry from this mission's bound flight controller is required.");
        if (!online) reasons.Add("Keep an authenticated, visible browser tab online.");
        if (!freshPreflight) reasons.Add("A fresh, passing preflight check for this exact plan is required.");
        if (!confirmed) reasons.Add("The pilot has not confirmed this exact plan and drone connection.");
        // Deliberate preparation boundary: no command transport/aircraft capability ACK exists yet.
        reasons.Add("Aircraft command execution is not enabled. Confirmation does not arm or launch the drone.");
        return new(id, hash, now, start, InConfirmationWindow(now, start), connection != null,
            online, confirmed, false, reasons.ToArray());
    }

    public async Task<FlightPreparation> PresenceAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var mission = await OwnedMission(id, actor, ct);
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
        if (!InConfirmationWindow(now, start)) return await ReadAsync(id, actor, ct);
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
            throw new InvalidOperationException("Refresh the plan. Confirmation requires the five-minute window, an online operator and fresh telemetry.");
        var result = await validation.RevalidateAsync(id, actor, false, "Preflight", ct);
        if (!result.IsValid || result.Error || result.Stale || result.Incomplete)
            throw new InvalidOperationException("Preflight did not pass: " + result.Message);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var mission = await OwnedMission(id, actor, ct);
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime!.Value, DateTimeKind.Utc));
            var hash = MissionValidationService.Hash(mission);
            var connection = await Connected(mission, now, ct);
            var runMatches = await db.MissionValidationRuns.AnyAsync(r => r.Id == result.Id && r.MissionId == id && r.PlanHash == hash && r.IsValid, ct);
            var state = await db.MissionAutomationStates.SingleAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
            if (hash != expectedHash || !runMatches || connection == null || !InConfirmationWindow(now, start)
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
            maxSpeedMetersPerSecond = mission.MaxSpeedMetersPerSecond,
            supportedFirmwareTargets = new[] { "ArduPilot", "PX4" },
            waypoints = mission.Waypoints.OrderBy(w => w.SequenceOrder).Select(w => new
            {
                sequenceOrder = w.SequenceOrder, latitudeDegrees = w.Latitude, longitudeDegrees = w.Longitude,
                latitudeE7 = (int)decimal.Round(w.Latitude * 10000000m, 0, MidpointRounding.AwayFromZero),
                longitudeE7 = (int)decimal.Round(w.Longitude * 10000000m, 0, MidpointRounding.AwayFromZero),
                altitudeMeters = w.Altitude, action = w.ActionType
            }),
            requiredBeforeDispatch = new[] { "Detected firmware/version/vehicle type and capabilities", "Explicit altitude frame and home/terrain data",
                "Per-action yaw/hold/camera parameters", "Failsafe/RTL configuration", "Upload ACK and read-back plan hash", "Fresh pre-arm checks and command acknowledgements" }
        };
    }
}
