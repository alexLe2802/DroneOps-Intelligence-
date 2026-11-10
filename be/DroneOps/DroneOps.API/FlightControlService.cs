using System.Data;
using System.Text.Json;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API;

public sealed class FlightControlService(AppDbContext db, MissionAutomationService preparation, IConfiguration configuration, TimeProvider? time = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool Enabled => configuration.GetValue<bool>("FlightExecution:Enabled");
    private DateTimeOffset Now => (time ?? TimeProvider.System).GetUtcNow();

    public Task<object> ReadAsync(Guid id, Guid actor, bool presence, CancellationToken ct) => Locked(id, actor, async (mission, row, state) =>
    {
        var now = Now;
        var ready = await preparation.ReadAsync(id, actor, ct);
        state.Tick(now, Authorized(ready, now) && Enabled && EnvelopeVerified(state));
        if (presence) state.PilotAt = now;
        var sample = ready.DroneConnected && state.Fresh(now)
            ? await db.TelemetryRecords.AsNoTracking().Where(t => t.MissionId == id && t.UAVId == mission.UAVId && t.IsValid
                && t.SourceTimestamp >= now.AddSeconds(-3) && t.SourceTimestamp <= now && t.ReceivedAt >= now.AddSeconds(-3) && t.ReceivedAt <= now)
                .OrderByDescending(t => t.SourceTimestamp).FirstOrDefaultAsync(ct) : null;
        return View(mission, state, ready, now, sample);
    }, ct);

    public Task<object> ConfigureAsync(Guid id, Guid actor, string hash, MissionCompileOptions options, CancellationToken ct) => Locked(id, actor, async (mission, row, state) =>
    {
        if (mission.Status != "Approved" || state.DispatchAttemptedAt != null)
            throw new InvalidOperationException("Execution settings are frozen once dispatch has been attempted.");
        if (state.Stage is "Arming" or "Countdown" or "Starting" or "AwaitingFlight" or "Running" or "Disarming" || state.Evidence?.Armed == true)
            throw new InvalidOperationException("Mission settings cannot change while an aircraft command or armed flight is active.");
        var compiled = MissionCommandCompiler.Compile(mission, options, state.Evidence?.FirmwareParameters);
        if (mission.EndTime is not { } end || end <= Now.UtcDateTime) throw new InvalidOperationException("The mission has ended.");
        if (hash != compiled.SourcePlanHash) throw new InvalidOperationException("The mission changed. Refresh and review it again.");
        if (!compiled.CanEncode) throw new InvalidOperationException(string.Join(" ", compiled.Errors));
        if (compiled.Items.Count > 500) throw new InvalidOperationException("The experimental FC uploader supports at most 500 compiled mission items.");
        state.Cancel("Mission settings changed.");
        state.WaypointCommands = compiled.WaypointCommands?.ToList() ?? [];
        state.Options = options; state.PlanHash = compiled.SourcePlanHash; state.CompiledHash = compiled.CompiledMissionHash;
        state.UploadVerified = false; state.Stage = "AwaitingGateway"; state.Message = "Waiting for the bound FC to connect, disarmed and on the ground, for upload.";
        row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null;
        return View(mission, state, await preparation.ReadAsync(id, actor, ct), Now);
    }, ct);

    public Task<object> ActAsync(Guid id, Guid actor, string action, Guid operationId, string hash, CancellationToken ct) => Locked(id, actor, async (mission, row, state) =>
    {
        var now = Now;
        var ready = await preparation.ReadAsync(id, actor, ct);
        if (operationId == Guid.Empty) throw new InvalidOperationException("An operation ID is required.");
        if (state.Commands.Any(c => c.Id == operationId)) return View(mission, state, ready, now);
        state.Tick(now, Authorized(ready, now) && Enabled && EnvelopeVerified(state));
        if (!Enabled) throw new InvalidOperationException("Aircraft commands are disabled. Enable FlightExecution:Enabled on the backend and --enable-flight-control on the gateway.");
        if (hash != state.CompiledHash) throw new InvalidOperationException("The FC plan changed. Refresh and review again.");
        if (!state.OnGround(now)) throw new InvalidOperationException("Fresh FC ON_GROUND state is required. In-air disarm is blocked.");
        if (action == "Arm")
            throw new InvalidOperationException("Confirm before the scheduled start; automatic ARM is dispatched by the scheduler.");
        else if (action == "Disarm")
        {
            if (state.Stage is "Starting" or "Running" || state.Evidence?.Armed != true)
                throw new InvalidOperationException("DISARM is available only while FC reports armed on the ground, before mission start.");
            state.Cancel("Pilot requested DISARM.");
        }
        else throw new InvalidOperationException("Unsupported aircraft action.");
        state.Queue(action, now, operationId);
        return View(mission, state, ready, now);
    }, ct);

    // Invoked only after the existing gateway token + hardware UID validation.
    public async Task<object> PollAsync(Guid connectionId, FlightControllerEvidence evidence, FlightControlReport? report, CancellationToken ct)
    {
        var connection = await db.DroneConnections.AsNoTracking().SingleAsync(c => c.Id == connectionId, ct);
        var observedNow = Now;
        if (connection.RevokedAt != null || connection.ExpiresAt <= observedNow)
            throw new InvalidOperationException("Connection is no longer eligible for telemetry.");
        // Parameter discovery is useful for manual missions too. Keep it outside
        // the automatic-execution lock, after gateway identity validation.
        if (evidence.FirmwareParameters is { } candidate && (candidate.Values == null || candidate.Values.Count > 64
            || string.IsNullOrWhiteSpace(candidate.FirmwareVersion) || candidate.FirmwareVersion.Length > 64
            || candidate.ObservedAtUtc > observedNow.AddSeconds(2) || candidate.ObservedAtUtc < observedNow.AddMinutes(-1)))
            evidence = evidence with { FirmwareParameters = null };
        if (evidence.FirmwareParameters is { } snapshot && snapshot.ObservedAtUtc <= observedNow.AddSeconds(2)
            && snapshot.ObservedAtUtc >= observedNow.AddMinutes(-1) && snapshot.Values.Count <= 64)
        {
            var trustedSnapshot = snapshot with { HardwareIdentity = connection.HardwareIdentity, Source = "FC MAVLink (planning cache; reverify before ARM)" };
            var cachedJson = JsonSerializer.Serialize(trustedSnapshot, Json);
            await db.UAVs.Where(u => u.Id == connection.UavId).ExecuteUpdateAsync(u => u.SetProperty(x => x.FirmwareNavigationDefaultsJson, cachedJson), ct);
        }
        return await Locked(connection.MissionId, connection.OwnerId, async (mission, row, state) =>
        {
            var now = Now;
            if (mission.UAVId != connection.UavId || connection.RevokedAt != null || connection.ExpiresAt <= now)
                throw new InvalidOperationException("Connection is no longer eligible for flight control.");
            if (evidence.GatewayInstance == Guid.Empty) throw new InvalidOperationException("Gateway instance is required.");
            if (state.ConnectionId != connectionId || state.GatewayInstance != evidence.GatewayInstance || state.PlanHash != MissionValidationService.Hash(mission))
            {
                state.Cancel("Gateway, connection or mission changed. Confirm again after verification.");
                state.UploadVerified = false; state.ConnectionId = connectionId; state.GatewayInstance = evidence.GatewayInstance;
                row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null;
            }
            state.Evidence = evidence; state.EvidenceAt = now;
            // Resolve optional authoring data after connection, not by asking the
            // pilot to invent autopilot parameters or a remote landing elevation.
            var landingGround = mission.Waypoints.OrderBy(w => w.SequenceOrder).LastOrDefault()?.Navigation?.LandingGroundAltitudeMeters;
            if (mission.Status == "Approved" && state.Options == null && evidence.Firmware is "ArduPilot" or "PX4" && evidence.Armed == false
                && evidence.LandedState == 1 && (evidence.Firmware == "ArduPilot" || landingGround != null))
            {
                var automaticOptions = new MissionCompileOptions(evidence.Firmware, YawPolicy: "Autopilot", LandingGroundAltitudeMeters: landingGround,
                    ConfigureRecovery: configuration.GetValue<bool>("FlightExecution:ConfigureRecovery"));
                var resolved = MissionCommandCompiler.Compile(mission, automaticOptions, evidence.FirmwareParameters);
                if (resolved.CanEncode)
                {
                    state.WaypointCommands = resolved.WaypointCommands?.ToList() ?? [];
                    state.Options = automaticOptions; state.PlanHash = resolved.SourcePlanHash; state.CompiledHash = resolved.CompiledMissionHash;
                    state.UploadVerified = false; state.Stage = "AwaitingGateway";
                    state.Message = "Approved waypoint route resolved; waiting for FC flight-limit verification.";
                    row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null;
                }
            }
            var ready = await preparation.ReadAsync(mission.Id, mission.CreatedBy, ct);
            var authorized = Authorized(ready, now) && Enabled && EnvelopeVerified(state) && row.ConfirmedPlanHash == ready.PlanHash && row.ConnectionId == connectionId;
            state.Tick(now, authorized);
            if (report != null) state.Complete(report.CommandId, report.Succeeded, report.UploadedHash, now);
            await ReconcileAsync(mission, connection, state, evidence, now, ct);
            // Cancellation/expiry never becomes a new upload/start automatically.
            var compiled = state.Options == null ? null : MissionCommandCompiler.Compile(mission, state.Options, evidence.FirmwareParameters);
            var compatible = compiled?.CanEncode == true && compiled.CompiledMissionHash == state.CompiledHash
                && evidence.Firmware == state.Options?.FirmwareTarget;
            if (state.UploadVerified && evidence.UploadedHash != state.CompiledHash)
            {
                state.Cancel("FC mission verification was lost."); state.UploadVerified = false;
            }
            if (Enabled && compatible && !EnvelopeVerified(state) && !state.UploadVerified && state.Pending == null)
            {
                state.Stage = "AwaitingFcProfile";
                state.Message = evidence.FcProfileError ?? "Waiting for the gateway to apply and read back this exact FC operating envelope.";
            }
            if (mission.Status == "Approved" && Enabled && now < ready.ScheduledAt && compatible && EnvelopeVerified(state) && !state.UploadVerified && state.Pending == null && state.OnGround(now)
                && evidence.Armed == false && state.Stage is "AwaitingGateway" or "AwaitingFcProfile" or "NeedsPlan" or "Cancelled")
                state.Queue("Upload", now);
            if (mission.Status == "Approved" && state.DispatchAttemptedAt == null && now >= ready.ScheduledAt)
                Dispatch(mission, state, ready, now);
            if (state.Stage == "Countdown" && now < ready.ScheduledAt.AddSeconds(5) && authorized && compatible && state.UploadVerified
                && state.LaunchAt is { } launch && now >= launch && state.Pending == null)
            {
                var startCommand = state.Queue("Start", now);
                startCommand.ExpiresAt = ready.ScheduledAt.AddSeconds(5);
            }
            FlightControlCommand? outgoing = null;
            if (state.Pending is { DeliveredAt: null } pending)
            {
                // Mark before writing to the socket. A lost response has an unknown outcome,
                // never a replayable command. Start also requires a current pilot lease.
                if (pending.Action is "Arm" or "Start" && (!authorized || !state.PilotPresent(now) || !compatible)) state.Cancel("Command authorization expired.");
                else { pending.DeliveredAt = now; outgoing = pending; }
            }
            var leaseActive = Enabled && (state.Stage == "Running" || now < ready.EndsAt)
                && (state.Stage is "Running" or "AwaitingFlight" || state.Stage is not ("Arming" or "Countdown" or "Starting") || state.PilotPresent(now) && ready.OperatorOnline);
            return new { enabled = Enabled, serverNow = now, missionId = mission.Id, connectionId,
                controllerUid = connection.HardwareIdentity, planHash = state.PlanHash, adapterVersion = "arducopter-4.7.1-v1",
                scheduledAtUtc = ready.ScheduledAt, dispatchId = state.DispatchId, leaseSeconds = leaseActive ? 3 : 0, stage = state.Stage, launchAt = state.LaunchAt,
                compiledHash = state.CompiledHash, command = outgoing,
                operatingEnvelope = compiled?.OperationalEnvelope,
                plannedHome = compiled?.PlannedHome,
                requiredEnvelopeHash = state.CompiledHash,
                mission = outgoing?.Action == "Upload" ? compiled : null };
        }, ct);
    }

    public async Task DispatchDueAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        var now = Now.UtcDateTime;
        var due = await db.Missions.AsNoTracking().Where(m => m.AutomaticFlightRequested && m.Status == "Approved"
            && m.StartTime <= now && m.EndTime > now).Select(m => new { m.Id, m.CreatedBy }).ToListAsync(ct);
        foreach (var candidate in due)
            await Locked(candidate.Id, candidate.CreatedBy, async (mission, row, state) =>
            {
                if (state.DispatchAttemptedAt == null)
                    Dispatch(mission, state, await preparation.ReadAsync(mission.Id, mission.CreatedBy, ct), Now);
                return (object)state.Stage;
            }, ct);
    }

    private void Dispatch(Mission mission, FlightControlState state, FlightPreparation ready, DateTimeOffset now)
    {
        if (state.DispatchAttemptedAt != null) return;
        state.DispatchAttemptedAt = now; state.DispatchDeadline = ready.ScheduledAt.AddSeconds(5);
        state.DispatchId = Guid.NewGuid();
        if (now >= state.DispatchDeadline || !CanArm(state, ready, now))
        {
            state.Stage = now >= state.DispatchDeadline ? "Expired" : "Blocked";
            state.Message = "Scheduled dispatch was blocked or expired. No catch-up launch is permitted.";
            return;
        }
        state.ApprovedPlanJson = JsonSerializer.Serialize(MissionPlans.Dto(mission), Json);
        state.Queue("Arm", now, state.DispatchId).ExpiresAt = state.DispatchDeadline.Value;
    }

    private async Task ReconcileAsync(Mission mission, DroneConnection connection, FlightControlState state,
        FlightControllerEvidence evidence, DateTimeOffset now, CancellationToken ct)
    {
        var execution = await db.MissionFlightExecutions.SingleOrDefaultAsync(e => e.MissionId == mission.Id, ct);
        var started = state.Commands.Any(c => c.Action == "Start" && c.DeliveredAt != null);
        var bound = state.ConnectionId == connection.Id && state.CompiledHash != null && evidence.UploadedHash == state.CompiledHash;
        if (!bound) return; // Unknown outcome never authorizes another start.
        if (execution == null && started && evidence.Armed == true && evidence.LandedState == 2 && evidence.MissionMode
            && state.ApprovedPlanJson != null && state.WaypointCommands.Count > 0)
        {
            if (await db.MissionFlightExecutions.AnyAsync(e => e.UavId == connection.UavId && e.CompletedAt == null, ct))
                throw new InvalidOperationException("Aircraft already has an active execution.");
            execution = new MissionFlightExecution { MissionId = mission.Id, UavId = connection.UavId,
                ConnectionId = connection.Id, PlanHash = state.PlanHash!, CompiledHash = state.CompiledHash!,
                ApprovedPlanJson = state.ApprovedPlanJson, WaypointCommandsJson = JsonSerializer.Serialize(state.WaypointCommands),
                StartedAt = now };
            db.MissionFlightExecutions.Add(execution);
            mission.Status = "InProgress"; state.FlightObservedAt = now;
            state.Stage = "Running"; state.Message = "Trusted FC evidence confirms in-air mission execution.";
        }
        if (execution == null || execution.CompletedAt != null) return;
        if (evidence.Armed == true && evidence.LandedState == 2) state.Stage = "Running";
        var planned = JsonSerializer.Deserialize<int[]>(execution.WaypointCommandsJson)!;
        var reached = (JsonSerializer.Deserialize<int[]>(execution.ReachedCommandsJson) ?? []).ToHashSet();
        var approved = JsonSerializer.Deserialize<DroneOps.Application.DTOs.Request.Missions.CreateMissionRequest>(execution.ApprovedPlanJson, Json);
        var returnsHome = string.Equals(approved?.Waypoints.OrderBy(w => w.SequenceOrder).LastOrDefault()?.ActionType, "Return", StringComparison.OrdinalIgnoreCase);
        foreach (var seq in evidence.ReachedCommands ?? [])
            if (planned.Contains(seq) && (!returnsHome || seq != planned[^1]) || returnsHome && seq == planned[^1] - 1)
                reached.Add(seq);
        if (evidence.LandedObservedAtUtc is { } observed && observed <= now && now - observed <= TimeSpan.FromSeconds(3)
            && (state.LastTerminalObservationAt == null || observed > state.LastTerminalObservationAt))
        {
            state.LandedSamples = evidence.LandedState == 1 && evidence.Armed == false ? state.LandedSamples + 1 : 0;
            state.LastTerminalObservationAt = observed;
        }
        if (state.LandedSamples >= 2)
        {
            var allBeforeLand = planned.SkipLast(1).All(reached.Contains);
            var atTerminal = evidence.MissionCurrent >= planned[^1];
            var completed = allBeforeLand && (returnsHome
                ? reached.Contains(planned[^1] - 1) && (atTerminal || (evidence.ReachedCommands ?? []).Contains(planned[^1]))
                : atTerminal || reached.Contains(planned[^1]));
            if (completed && returnsHome)
            {
                var position = await db.TelemetryRecords.AsNoTracking().Where(t => t.MissionId == mission.Id && t.IsValid
                    && t.SourceTimestamp >= now.AddSeconds(-5) && t.SourceTimestamp <= now
                    && t.ReceivedAt >= now.AddSeconds(-5) && t.ReceivedAt <= now)
                    .OrderByDescending(t => t.SourceTimestamp).FirstOrDefaultAsync(ct);
                completed = position?.Latitude is { } latitude && position.Longitude is { } longitude
                    && evidence.HomeLatitudeDegrees is { } homeLat && evidence.HomeLongitudeDegrees is { } homeLon
                    && MissionRouteGeometry.DistanceMeters(latitude, longitude, homeLat, homeLon) <= 10;
            }
            if (completed) reached.Add(planned[^1]);
            execution.CompletedAt = now; execution.Outcome = completed ? "Completed" : "Aborted";
            mission.Status = execution.Outcome; state.Stage = execution.Outcome;
            state.Message = completed ? "The verified waypoint route finished and FC confirmed landing." : "Flight landed before all planned waypoints were confirmed; partial evidence retained.";
            if (!await db.FlightCompletionEvents.AnyAsync(e => e.MissionId == mission.Id && e.IsTrusted, ct))
            {
                db.FlightCompletionEvents.Add(new FlightCompletionEvent { Id = Guid.NewGuid(), MissionId = mission.Id,
                    UAVId = connection.UavId, SourceId = $"gateway-{connection.Id:N}",
                    IdempotencyKey = $"execution-terminal:{mission.Id:N}", EventType = completed ? "MISSION_COMPLETED" : "MISSION_ABORTED",
                    CompletedAt = now, ReceivedAt = now, IsTrusted = true,
                    Evidence = JsonSerializer.Serialize(new { state.DispatchId, execution.CompiledHash, landedState = evidence.LandedState, evidence.Armed, reached, evidence.MissionCurrent }, Json) });
                db.PostFlightReports.Add(new PostFlightReport { Id = Guid.NewGuid(), MissionId = mission.Id,
                    Status = execution.Outcome, GenerationStatus = "Queued", CoveredTelemetryFrom = execution.StartedAt,
                    CoveredTelemetryThrough = now, CreatedAt = now, GenerationReason = "Trusted FC terminal observation" });
            }
        }
        execution.ReachedCommandsJson = JsonSerializer.Serialize(reached.Order().ToArray());
    }

    private bool CanArm(FlightControlState state, FlightPreparation ready, DateTimeOffset now) => Enabled && Authorized(ready, now)
        && EnvelopeVerified(state)
        && now >= ready.ScheduledAt && now < ready.ScheduledAt.AddSeconds(5) && state.PilotPresent(now) && state.UploadVerified && state.OnGround(now)
        && state.Evidence is { Armed: false, NavigationReady: true, PreArmReady: true }
        && state.Evidence.Firmware == "ArduPilot" && state.Options?.FirmwareTarget == "ArduPilot" && state.Evidence.UploadedHash == state.CompiledHash
        && state.Stage is ("Ready" or "Cancelled") && state.Pending == null && state.PlanHash == ready.PlanHash;

    private static bool Authorized(FlightPreparation ready, DateTimeOffset now) => ready.ReadinessConfirmed
        && ready.DroneConnected && ready.OperatorOnline && now >= ready.ScheduledAt && now < ready.ScheduledAt.AddSeconds(5);

    private static bool EnvelopeVerified(FlightControlState state) => state.CompiledHash != null && state.Evidence?.VerifiedEnvelopeHash == state.CompiledHash;

    private object View(Mission mission, FlightControlState state, FlightPreparation ready, DateTimeOffset now, TelemetryRecord? sample = null) => new {
        recoveryAssessment = RecoveryEnergyCalculator.Assess(mission, sample,
            state.Fresh(now) && state.Evidence?.HomeLatitudeDegrees is { } lat && state.Evidence.HomeLongitudeDegrees is { } lon
                ? new RecoveryPosition(lat, lon, 0) : null, now),
        readinessChecks = new[] {
            new { key = "confirmation", label = "Pilot confirmation", satisfied = ready.ReadinessConfirmed, detail = ready.ReadinessConfirmed ? "Current plan and connection confirmed" : "Confirm the current mission in Live Monitoring" },
            new { key = "connection", label = "Bound drone connection", satisfied = ready.DroneConnected, detail = ready.DroneConnected ? "Fresh bound connection" : "Connect the assigned drone" },
            new { key = "operator", label = "Pilot online", satisfied = ready.OperatorOnline, detail = "Keep Live Monitoring visible and online" },
            new { key = "preflight", label = "Preflight validation", satisfied = ready.PreflightReady, detail = "Fresh passing validation for this exact plan" },
            new { key = "fc-profile", label = "FC flight limits", satisfied = state.Fresh(now) && state.PlanHash == ready.PlanHash && EnvelopeVerified(state), detail = state.Evidence?.FcProfileError ?? "Gateway applies and reads back the supported FC profile before upload" },
            new { key = "upload", label = "Mission read-back", satisfied = state.Fresh(now) && state.PlanHash == ready.PlanHash && state.UploadVerified, detail = "Uploaded items must match the compiled plan" }
        },
        enabled = Enabled, serverNow = now, scheduledAt = ready.ScheduledAt, endsAt = ready.EndsAt,
        state.Stage, state.Message, state.Options, state.CompiledHash, state.UploadVerified, state.LaunchAt,
        armed = state.Fresh(now) ? state.Evidence?.Armed : null,
        homeLatitudeDegrees = state.Fresh(now) ? state.Evidence?.HomeLatitudeDegrees : null,
        homeLongitudeDegrees = state.Fresh(now) ? state.Evidence?.HomeLongitudeDegrees : null,
        homeAltitudeMslMeters = state.Fresh(now) ? state.Evidence?.HomeAltitudeMslMeters : null,
        onGround = state.OnGround(now), canArm = CanArm(state, ready, now),
        canDisarm = Enabled && state.OnGround(now) && state.Evidence?.Armed == true && state.Stage is not ("Starting" or "Running" or "Disarming"),
        reason = Enabled ? null : "Aircraft commands are disabled. Enable FlightExecution:Enabled on the backend and --enable-flight-control on the gateway."
    };

    private async Task<object> Locked(Guid id, Guid actor, Func<Mission, MissionAutomationState, FlightControlState, Task<object>> action, CancellationToken ct)
    {
        object? response = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            // Every writer of execution/consent takes this same mission row lock.
            // READ COMMITTED sees the preceding writer after waiting for the lock.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Mission\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var mission = await db.Missions.Include(m => m.Waypoints).SingleOrDefaultAsync(m => m.Id == id && m.CreatedBy == actor, ct)
                ?? throw new KeyNotFoundException("Mission not found.");
            if (!mission.AutomaticFlightRequested || mission.Status is not ("Approved" or "InProgress" or "Completed" or "Aborted") || mission.StartTime == null)
                throw new InvalidOperationException("Flight control requires an approved automatic mission.");
            var row = await db.MissionAutomationStates.SingleOrDefaultAsync(s => s.MissionId == id && s.OwnerId == actor, ct);
            if (row == null) { row = new() { MissionId = id, OwnerId = actor }; db.Add(row); }
            var state = row.ExecutionJson == null ? new() : JsonSerializer.Deserialize<FlightControlState>(row.ExecutionJson, Json) ?? new();
            response = await action(mission, row, state);
            row.ExecutionJson = JsonSerializer.Serialize(state, Json);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        return response!;
    }
}

public sealed record FlightControlReport(Guid CommandId, bool Succeeded, string? UploadedHash);
