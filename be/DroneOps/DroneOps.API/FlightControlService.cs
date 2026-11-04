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
        return View(state, ready, now);
    }, ct);

    public Task<object> ConfigureAsync(Guid id, Guid actor, string hash, MissionCompileOptions options, CancellationToken ct) => Locked(id, actor, async (mission, row, state) =>
    {
        if (state.Stage is "Arming" or "Countdown" or "Starting" or "Running" or "Disarming" || state.Evidence?.Armed == true)
            throw new InvalidOperationException("Mission settings cannot change while an aircraft command or armed flight is active.");
        var compiled = MissionCommandCompiler.Compile(mission, options, state.Evidence?.FirmwareParameters);
        if (mission.EndTime is not { } end || end <= Now.UtcDateTime) throw new InvalidOperationException("The mission has ended.");
        if (hash != compiled.SourcePlanHash) throw new InvalidOperationException("The mission changed. Refresh and review it again.");
        if (!compiled.CanEncode) throw new InvalidOperationException(string.Join(" ", compiled.Errors));
        if (compiled.Items.Count > 500) throw new InvalidOperationException("The experimental FC uploader supports at most 500 compiled mission items.");
        state.Cancel("Mission settings changed.");
        state.Options = options; state.PlanHash = compiled.SourcePlanHash; state.CompiledHash = compiled.CompiledMissionHash;
        state.UploadVerified = false; state.Stage = "AwaitingGateway"; state.Message = "Waiting for the bound FC to connect, disarmed and on the ground, for upload.";
        row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null;
        return View(state, await preparation.ReadAsync(id, actor, ct), Now);
    }, ct);

    public Task<object> ActAsync(Guid id, Guid actor, string action, Guid operationId, string hash, CancellationToken ct) => Locked(id, actor, async (mission, row, state) =>
    {
        var now = Now;
        var ready = await preparation.ReadAsync(id, actor, ct);
        if (operationId == Guid.Empty) throw new InvalidOperationException("An operation ID is required.");
        if (state.Commands.Any(c => c.Id == operationId)) return View(state, ready, now);
        state.Tick(now, Authorized(ready, now) && Enabled && EnvelopeVerified(state));
        if (!Enabled) throw new InvalidOperationException("Aircraft commands are disabled pending firmware SITL and failsafe verification.");
        if (hash != state.CompiledHash) throw new InvalidOperationException("The FC plan changed. Refresh and review again.");
        if (!state.OnGround(now)) throw new InvalidOperationException("Fresh FC ON_GROUND state is required. In-air disarm is blocked.");
        if (action == "Arm")
        {
            if (!CanArm(state, ready, now)) throw new InvalidOperationException("ARM requires StartTime, current confirmation, verified upload, online pilot and passing FC readiness.");
            state.PilotAt = now;
        }
        else if (action == "Disarm")
        {
            if (state.Stage is "Starting" or "Running" || state.Evidence?.Armed != true)
                throw new InvalidOperationException("DISARM is available only while FC reports armed on the ground, before mission start.");
            state.Cancel("Pilot requested DISARM.");
        }
        else throw new InvalidOperationException("Unsupported aircraft action.");
        state.Queue(action, now, operationId);
        return View(state, ready, now);
    }, ct);

    // Invoked only after the existing gateway token + hardware UID validation.
    public async Task<object> PollAsync(Guid connectionId, FlightControllerEvidence evidence, FlightControlReport? report, CancellationToken ct)
    {
        var connection = await db.DroneConnections.AsNoTracking().SingleAsync(c => c.Id == connectionId, ct);
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
            if (evidence.FirmwareParameters is { } candidate && (candidate.Values == null || candidate.Values.Count > 32
                || string.IsNullOrWhiteSpace(candidate.FirmwareVersion) || candidate.FirmwareVersion.Length > 64
                || candidate.ObservedAtUtc > now.AddSeconds(2) || candidate.ObservedAtUtc < now.AddMinutes(-1)))
                evidence = evidence with { FirmwareParameters = null };
            state.Evidence = evidence; state.EvidenceAt = now;
            if (evidence.FirmwareParameters is { } snapshot && snapshot.ObservedAtUtc <= now.AddSeconds(2)
                && snapshot.ObservedAtUtc >= now.AddMinutes(-1) && snapshot.Values.Count <= 32)
            {
                var trustedSnapshot = snapshot with { HardwareIdentity = connection.HardwareIdentity, Source = "FC MAVLink (planning cache; reverify before ARM)" };
                var cachedJson = JsonSerializer.Serialize(trustedSnapshot, Json);
                await db.UAVs.Where(u => u.Id == connection.UavId).ExecuteUpdateAsync(u => u.SetProperty(x => x.FirmwareNavigationDefaultsJson, cachedJson), ct);
            }
            // Resolve optional authoring data after connection, not by asking the
            // pilot to invent autopilot parameters or a remote landing elevation.
            var landingGround = mission.Waypoints.OrderBy(w => w.SequenceOrder).LastOrDefault()?.Navigation?.LandingGroundAltitudeMeters;
            if (state.Options == null && evidence.Firmware is "ArduPilot" or "PX4" && evidence.Armed == false
                && evidence.LandedState == 1 && landingGround != null)
            {
                var automaticOptions = new MissionCompileOptions(evidence.Firmware, YawPolicy: "Autopilot", LandingGroundAltitudeMeters: landingGround);
                var resolved = MissionCommandCompiler.Compile(mission, automaticOptions, evidence.FirmwareParameters);
                if (resolved.CanEncode)
                {
                    state.Options = automaticOptions; state.PlanHash = resolved.SourcePlanHash; state.CompiledHash = resolved.CompiledMissionHash;
                    state.UploadVerified = false; state.Stage = "AwaitingGateway";
                    state.Message = "Approved waypoint options resolved; FC operating-envelope verification is still required.";
                    row.ConfirmedAt = null; row.ConfirmedPlanHash = null; row.ConnectionId = null;
                }
            }
            var ready = await preparation.ReadAsync(mission.Id, mission.CreatedBy, ct);
            var authorized = Authorized(ready, now) && Enabled && EnvelopeVerified(state) && row.ConfirmedPlanHash == ready.PlanHash && row.ConnectionId == connectionId;
            state.Tick(now, authorized);
            if (report != null) state.Complete(report.CommandId, report.Succeeded, report.UploadedHash, now);
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
                state.Message = "Waiting for a versioned gateway adapter to apply/read back this exact FC operating envelope. No upload or ARM is authorized.";
            }
            if (Enabled && now < ready.EndsAt && compatible && EnvelopeVerified(state) && !state.UploadVerified && state.Pending == null && state.OnGround(now)
                && evidence.Armed == false && state.Stage is "AwaitingGateway" or "AwaitingFcProfile" or "NeedsPlan" or "Cancelled")
                state.Queue("Upload", now);
            if (state.Stage == "Countdown" && authorized && compatible && state.UploadVerified
                && state.LaunchAt is { } launch && now >= launch && state.Pending == null)
                state.Queue("Start", now);
            FlightControlCommand? outgoing = null;
            if (state.Pending is { DeliveredAt: null } pending)
            {
                // Mark before writing to the socket. A lost response has an unknown outcome,
                // never a replayable command. Start also requires a current pilot lease.
                if (pending.Action is "Arm" or "Start" && (!authorized || !state.PilotPresent(now) || !compatible)) state.Cancel("Command authorization expired.");
                else { pending.DeliveredAt = now; outgoing = pending; }
            }
            var leaseActive = Enabled && now < ready.EndsAt
                && (state.Stage is not ("Arming" or "Countdown" or "Starting" or "Running") || state.PilotPresent(now) && ready.OperatorOnline);
            return new { enabled = Enabled, serverNow = now, leaseSeconds = leaseActive ? 3 : 0, stage = state.Stage, launchAt = state.LaunchAt,
                compiledHash = state.CompiledHash, command = outgoing,
                operatingEnvelope = compiled?.OperationalEnvelope,
                requiredEnvelopeHash = state.CompiledHash,
                mission = outgoing?.Action == "Upload" ? compiled : null };
        }, ct);
    }

    private bool CanArm(FlightControlState state, FlightPreparation ready, DateTimeOffset now) => Enabled && Authorized(ready, now)
        && EnvelopeVerified(state)
        && now >= ready.ScheduledAt && state.PilotPresent(now) && state.UploadVerified && state.OnGround(now)
        && state.Evidence is { Armed: false, NavigationReady: true, PreArmReady: true }
        && state.Evidence.Firmware == state.Options?.FirmwareTarget && state.Evidence.UploadedHash == state.CompiledHash
        && state.Stage is ("Ready" or "Cancelled") && state.Pending == null && state.PlanHash == ready.PlanHash;

    private static bool Authorized(FlightPreparation ready, DateTimeOffset now) => ready.ReadinessConfirmed
        && ready.DroneConnected && ready.OperatorOnline && now >= ready.ScheduledAt && now < ready.EndsAt;

    private static bool EnvelopeVerified(FlightControlState state) => state.CompiledHash != null && state.Evidence?.VerifiedEnvelopeHash == state.CompiledHash;

    private object View(FlightControlState state, FlightPreparation ready, DateTimeOffset now) => new {
        enabled = Enabled, serverNow = now, scheduledAt = ready.ScheduledAt, endsAt = ready.EndsAt,
        state.Stage, state.Message, state.Options, state.CompiledHash, state.UploadVerified, state.LaunchAt,
        armed = state.Fresh(now) ? state.Evidence?.Armed : null,
        homeLatitudeDegrees = state.Fresh(now) ? state.Evidence?.HomeLatitudeDegrees : null,
        homeLongitudeDegrees = state.Fresh(now) ? state.Evidence?.HomeLongitudeDegrees : null,
        homeAltitudeMslMeters = state.Fresh(now) ? state.Evidence?.HomeAltitudeMslMeters : null,
        onGround = state.OnGround(now), canArm = CanArm(state, ready, now),
        canDisarm = Enabled && state.OnGround(now) && state.Evidence?.Armed == true && state.Stage is not ("Starting" or "Running" or "Disarming"),
        reason = Enabled ? null : "Aircraft commands are disabled until ArduPilot/PX4 SITL and the failsafe profile have been verified."
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
            var mission = await db.Missions.AsNoTracking().Include(m => m.Waypoints).SingleOrDefaultAsync(m => m.Id == id && m.CreatedBy == actor, ct)
                ?? throw new KeyNotFoundException("Mission not found.");
            if (!mission.AutomaticFlightRequested || mission.Status != "Approved" || mission.StartTime == null)
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
