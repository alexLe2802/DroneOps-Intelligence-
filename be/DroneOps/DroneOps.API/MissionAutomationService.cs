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
    DateTimeOffset EndsAt, bool GatewayConnected, bool PreflightReady, bool ConfirmationRecorded, bool PreArmReady, DateTimeOffset? ConfirmedAt, DateTimeOffset? ActualEndedAt);

public sealed class MissionAutomationService(AppDbContext db, IMissionValidationService validation, IConfiguration? configuration = null)
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
        if (requireApproved && (!mission.AutomaticFlightRequested || mission.Status is not ("Approved" or "InProgress" or "Completed" or "Aborted") || mission.StartTime is null))
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
        var windowOpen = mission.Status == "Approved" && InConfirmationWindow(now, start, end);
        var hash = MissionValidationService.Hash(mission);
        var connection = await Connected(mission, now, ct);
        var gatewayConnected = connection != null || await Connected(mission, now, ct, requireSample: false) != null;
        var state = await db.MissionAutomationStates.AsNoTracking().SingleOrDefaultAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
        var preflight = await db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == id && r.Stage == "Preflight")
            .OrderByDescending(r => r.CheckedAt).FirstOrDefaultAsync(ct);
        var freshPreflight = preflight is { IsValid: true, ResultStatus: "Pass" } && preflight.PlanHash == hash
            && preflight.CheckedAt >= now.AddMinutes(-1) && preflight.CheckedAt <= now;
        var online = state?.LastPresenceAt >= now.AddSeconds(-30) && state.LastPresenceAt <= now;
        var actualEndedAt = await db.MissionFlightExecutions.AsNoTracking().Where(e => e.MissionId == id)
            .Select(e => e.CompletedAt).SingleOrDefaultAsync(ct);
        var execution = state?.ExecutionJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<FlightControlState>(state.ExecutionJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var preArmReady = connection != null && execution != null && execution.ConnectionId == connection.Id && execution.Fresh(now) && execution.OnGround(now)
            && execution.Evidence is { Armed: false, NavigationReady: true, PreArmReady: true }
            && execution.UploadVerified && execution.PlanHash == hash && execution.CompiledHash != null
            && execution.Evidence.UploadedHash == execution.CompiledHash && execution.Evidence.VerifiedEnvelopeHash == execution.CompiledHash
            && execution.DispatchAttemptedAt == null && execution.Pending == null && execution.Stage is ("Ready" or "Cancelled")
            && execution.Evidence.Firmware == "ArduPilot" && execution.Options?.FirmwareTarget == "ArduPilot";
        var recorded = windowOpen && online && state?.ConfirmedPlanHash == hash && state.ConfirmedAt >= start.AddMinutes(-5)
            && state.ConfirmedAt <= now && state.ConfirmedAt < end
            && (now < start.AddSeconds(5) || state.ConfirmedAt >= start || execution?.DispatchAttemptedAt != null && now < execution.DispatchDeadline) && connection != null && state.ConnectionId == connection.Id;
        var pendingProposal = await db.MissionUpdateRequests.AsNoTracking().AnyAsync(r => r.MissionId == id && r.Status == "Pending", ct);
        var eligibilityBlock = await FlightEligibility.BlockingReasonAsync(db, mission, ct);
        var confirmed = recorded && freshPreflight && !pendingProposal && eligibilityBlock is null;
        var reasons = new List<string>();
        if (eligibilityBlock is not null) reasons.Add(eligibilityBlock);
        if (pendingProposal) reasons.Add("A pending mission change must be reviewed before dispatch.");
        if (!windowOpen && !recorded) reasons.Add(now >= end ? "The planned flight window has ended." : "Confirmation opens five minutes before the scheduled start.");
        if (connection == null) reasons.Add("Fresh telemetry from this mission's bound flight controller is required.");
        if (!online) reasons.Add("Keep an authenticated, visible browser tab online.");
        if (!freshPreflight) reasons.Add("A fresh, passing preflight check for this exact plan is required.");
        if (!recorded) reasons.Add("The pilot has not confirmed this exact plan and drone connection.");
        // This endpoint reports preparation consent; execution is a separate,
        // explicitly gated action in the same automation API.
        if (!confirmed) reasons.Add("Confirm the verified mission; automatic flight starts only while all readiness gates remain satisfied.");
        return new(id, hash, now, start, windowOpen, connection != null,
            online, confirmed, configuration?.GetValue<bool>("FlightExecution:Enabled") == true && confirmed && preArmReady && now >= start, reasons.ToArray(), end, gatewayConnected, freshPreflight, recorded, preArmReady, recorded ? state?.ConfirmedAt : null, actualEndedAt);
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
            throw new InvalidOperationException("Refresh the plan. Confirmation requires an approved mission within its flight window, an online operator and fresh telemetry.");
        if (configuration?.GetValue<bool>("FlightExecution:Enabled") == true && !before.PreArmReady)
            throw new InvalidOperationException("Fresh passing FC pre-arm checks, verified upload and a disarmed aircraft on the ground are required.");
        var result = await validation.RevalidateAsync(id, actor, false, "Preflight", ct);
        if (!result.IsValid || result.Error || result.Stale || result.Incomplete)
            throw new InvalidOperationException("Preflight did not pass: " + result.Message);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Mission\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var mission = await OwnedMission(id, actor, ct);
            await FlightEligibility.LockAircraftAsync(db, mission.UAVId, ct);
            var eligibilityBlock = await FlightEligibility.BlockingReasonAsync(db, mission, ct);
            if (eligibilityBlock is not null) throw new InvalidOperationException(eligibilityBlock);
            var lockedReadiness = await ReadAsync(id, actor, ct);
            if (mission.Status != "Approved" || configuration?.GetValue<bool>("FlightExecution:Enabled") == true && !lockedReadiness.PreArmReady)
                throw new InvalidOperationException("Aircraft readiness changed. Review pre-arm checks again.");
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
                altitudeMeters = w.Altitude, action = w.ActionType, navigation = w.Navigation
            }),
            requiredBeforeDispatch = new[] { "Detected firmware/version/vehicle type and capabilities", "Explicit altitude frame and home/terrain data",
                "Per-action yaw/hold parameters", "Failsafe/RTL configuration", "Upload ACK and read-back plan hash", "Fresh pre-arm checks and command acknowledgements" }
        };
    }
}
