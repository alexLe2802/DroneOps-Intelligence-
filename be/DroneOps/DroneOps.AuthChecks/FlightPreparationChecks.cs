using System.Text.Json;
using DroneOps.API;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Services.Telemetry;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal static class FlightPreparationChecks
{
    public static async Task RunAsync(string root)
    {
        // Fail closed: this suite can never run against Supabase or a real application DB.
        var text = Environment.GetEnvironmentVariable("DRONEOPS_FIXTURE_CONNECTION") ?? throw new InvalidOperationException("Set the isolated fixture connection.");
        var settings = new NpgsqlConnectionStringBuilder(text);
        if (settings.Host != "127.0.0.1" || settings.Port != 54339 || settings.Database != "droneops_fixture")
            throw new InvalidOperationException("Only the disposable localhost fixture database is allowed.");
        await using var source = NpgsqlDataSource.Create(text);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options);
        if (await db.Database.CanConnectAsync() && (await source.CreateCommand("SELECT count(*) FROM pg_tables WHERE schemaname='public'").ExecuteScalarAsync()) is long count && count != 0)
            throw new InvalidOperationException("Fixture database must be empty.");
        await db.Database.EnsureCreatedAsync();
        await source.CreateCommand("""
            DO $roles$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN CREATE ROLE anon NOLOGIN; END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN CREATE ROLE authenticated NOLOGIN; END IF;
            END $roles$;
            DROP TABLE public."MissionAutomationState";
            ALTER TABLE public."Mission" DROP COLUMN "AutomaticFlightRequested";
            """).ExecuteNonQueryAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(root, "database/migrations/20261104115300_gateway_enrollment_and_flight_preparation.sql"));
        await source.CreateCommand(sql).ExecuteNonQueryAsync();
        await source.CreateCommand(sql).ExecuteNonQueryAsync();
        await FlightPreparationSchema.CheckAsync(source);
        Check(true, "migration upgrades an existing schema and is idempotent");
        var rls = await source.CreateCommand("SELECT count(*) FROM pg_class WHERE relname IN ('GatewayEnrollment','MissionAutomationState') AND relrowsecurity").ExecuteScalarAsync();
        Check((long)rls! == 2, "both new server-only tables enforce RLS");

        var role = new Role { Id = Guid.NewGuid(), Name = "uav_operator", CreatedAt = DateTimeOffset.UtcNow };
        var owner = new User { Id = Guid.NewGuid(), RoleId = role.Id, Email = "fixture@example.invalid", FullName = "Fixture", PasswordHash = "not-a-password", CreatedAt = DateTimeOffset.UtcNow };
        var pilot = new Pilot { Id = Guid.NewGuid(), UserId = owner.Id, CreatedAt = DateTime.UtcNow };
        var uav = new UAV { Id = Guid.NewGuid(), PilotId = pilot.Id, Code = "FIXTURE", Name = "Fixture UAV", Status = "Active", CreatedAt = DateTimeOffset.UtcNow };
        var mission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner.Id, UAVId = uav.Id, Name = "Fixture mission", Status = "Approved", AutomaticFlightRequested = true,
            StartTime = DateTime.UtcNow.AddMinutes(4), EndTime = DateTime.UtcNow.AddMinutes(30), CreatedAt = DateTime.UtcNow, AltitudeReference = "Relative", AverageSpeedMetersPerSecond = 5, MaxSpeedMetersPerSecond = 8 };
        const string uid = "UID64-0000000000000042";
        var hardware = new DroneHardwareIdentity { UavId = uav.Id, Identity = uid, BoundBy = owner.Id, BoundAt = DateTimeOffset.UtcNow };
        var session = Session(uav.Id, mission.Id, owner.Id, uid);
        db.AddRange(role, owner, pilot, uav, mission, hardware, session);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var gateway = new TrustedGatewayStore(source);
        var credential = new GatewayCredential(session.Id, uav.Id, mission.Id, "fixture", "lease", DateTimeOffset.UtcNow, uid);
        var deviceKey = await gateway.EnrollAsync(credential, default);
        var stored = await source.CreateCommand("SELECT \"TokenHash\" FROM public.\"GatewayEnrollment\"").ExecuteScalarAsync();
        Check(stored is string storedHash && storedHash != deviceKey && storedHash == DroneConnectionService.Hash(deviceKey), "enrollment stores only the SHA256 hash");
        var lease = await gateway.ResumeAsync(uav.Id, deviceKey, uid, default);
        Check(lease?.ConnectionId == session.Id && lease.ExpiresAt > DateTimeOffset.UtcNow.AddHours(11), "a trusted reboot renews the expired mission lease without a new pairing code");
        await Reject<UnauthorizedAccessException>(() => gateway.ResumeAsync(uav.Id, new string('A', 64), uid, default), "forged device credential is rejected");
        await Reject<UnauthorizedAccessException>(() => gateway.ResumeAsync(uav.Id, deviceKey, "UID64-0000000000000043", default), "a different flight controller cannot use the saved credential");
        await Reject<UnauthorizedAccessException>(() => gateway.ResumeAsync(Guid.NewGuid(), deviceKey, uid, default), "credential cannot be redirected to another registry UAV");
        await db.DroneConnections.Where(c => c.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, DateTimeOffset.UtcNow));
        Check(await gateway.ResumeAsync(uav.Id, deviceKey, uid, default) is null, "disconnect does not resurrect the revoked connection");
        var nextMission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner.Id, UAVId = uav.Id, Name = "Web-selected next mission", Status = "Approved", AutomaticFlightRequested = true,
            StartTime = DateTime.UtcNow.AddMinutes(4), EndTime = DateTime.UtcNow.AddMinutes(30), CreatedAt = DateTime.UtcNow, AltitudeReference = "Relative", AverageSpeedMetersPerSecond = 5, MaxSpeedMetersPerSecond = 8 };
        db.Add(nextMission); await db.SaveChangesAsync(); mission = nextMission;
        var selected = Session(uav.Id, mission.Id, owner.Id, uid); selected.PairedAt = null; selected.HardwareIdentity = null; selected.CreatedAt = DateTimeOffset.UtcNow;
        db.Add(selected); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var switched = await gateway.ResumeAsync(uav.Id, deviceKey, uid, default);
        Check(switched?.ConnectionId == selected.Id && switched.MissionId == mission.Id, "trusted gateway claims only the new mission connection explicitly selected on the web");
        await db.UAVs.Where(u => u.Id == uav.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Inactive"));
        await Reject<UnauthorizedAccessException>(() => gateway.ResumeAsync(uav.Id, deviceKey, uid, default), "inactive UAV cannot resume an enrolled gateway");
        await db.UAVs.Where(u => u.Id == uav.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Active"));

        await db.DroneConnections.Where(c => c.Id == selected.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSampleAt, DateTimeOffset.UtcNow));
        var preflight = new PreflightStub(db);
        var automation = new MissionAutomationService(db, preflight);
        await automation.PresenceAsync(mission.Id, owner.Id, default);
        await Reject<KeyNotFoundException>(() => automation.ReadAsync(mission.Id, Guid.NewGuid(), default), "another pilot cannot inspect this mission's preparation");
        var state = await automation.ReadAsync(mission.Id, owner.Id, default);
        Check(state.ConfirmationOpen && state.DroneConnected && state.OperatorOnline && !state.ReadinessConfirmed && !state.DispatchEnabled, "T-5 presence and telemetry do not imply consent or command dispatch");
        await Reject<InvalidOperationException>(() => automation.ConfirmAsync(mission.Id, owner.Id, "old-plan", default), "stale plan confirmation is rejected");
        preflight.Fail = true;
        await Reject<InvalidOperationException>(() => automation.ConfirmAsync(mission.Id, owner.Id, state.PlanHash, default), "failed preflight cannot be confirmed");
        preflight.Fail = false; db.ChangeTracker.Clear();
        var accepted = await automation.ConfirmAsync(mission.Id, owner.Id, state.PlanHash, default);
        Check(accepted.ReadinessConfirmed && !accepted.DispatchEnabled, "passing preflight stores exact-plan consent but cannot arm or launch");
        db.ChangeTracker.Clear();
        await db.Missions.Where(m => m.Id == mission.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxSpeedMetersPerSecond, 7m));
        Check(!(await automation.ReadAsync(mission.Id, owner.Id, default)).ReadinessConfirmed, "plan changes invalidate readiness confirmation");
        await db.Missions.Where(m => m.Id == mission.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.MaxSpeedMetersPerSecond, 8m));
        await db.MissionAutomationStates.Where(s => s.MissionId == mission.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastPresenceAt, DateTimeOffset.UtcNow.AddSeconds(-31)));
        Check(!(await automation.ReadAsync(mission.Id, owner.Id, default)).OperatorOnline, "operator presence expires after 30 seconds");
        var returned = await automation.PresenceAsync(mission.Id, owner.Id, default);
        Check(!returned.ReadinessConfirmed, "returning online does not silently restore a stale confirmation");
        db.ChangeTracker.Clear();
        await db.Missions.Where(m => m.Id == mission.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddSeconds(-1)));
        Check(!(await automation.ReadAsync(mission.Id, owner.Id, default)).ConfirmationOpen, "missed departure closes confirmation without a catch-up launch");
        await gateway.RevokeAsync(uav.Id, owner.Id, default);
        await Reject<UnauthorizedAccessException>(() => gateway.ResumeAsync(uav.Id, deviceKey, uid, default), "forget gateway revokes unattended access");
        Check(await db.DroneHardwareIdentities.AnyAsync(h => h.UavId == uav.Id && h.Identity == uid), "forgetting a gateway preserves the permanent physical hardware binding");
        Console.WriteLine("Isolated PostgreSQL enrollment/preparation checks passed. No production data or aircraft used.");
    }

    private static DroneConnection Session(Guid uav, Guid mission, Guid owner, string uid) => new()
    {
        Id = Guid.NewGuid(), UavId = uav, MissionId = mission, OwnerId = owner, HardwareIdentity = uid,
        PairCodeHash = DroneConnectionService.Hash(Guid.NewGuid().ToString()), TokenHash = DroneConnectionService.Hash("old-lease"),
        GatewayName = "fixture", SystemId = 42, ComponentId = 1, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        CodeExpiresAt = DateTimeOffset.UtcNow.AddHours(-20), ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        PairedAt = DateTimeOffset.UtcNow.AddDays(-1), LastGatewayAt = DateTimeOffset.UtcNow, LastSampleAt = DateTimeOffset.UtcNow
    };
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); Console.WriteLine("PASS: " + name); }
    private static async Task Reject<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); } catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("FAIL: " + name);
    }

    // Preflight decision is controlled here; weather/geometry validation has its own suite.
    private sealed class PreflightStub(AppDbContext db) : IMissionValidationService
    {
        public bool Fail { get; set; }
        public async Task<MissionValidationResult> RevalidateAsync(Guid id, Guid actor, bool manager, string stage, CancellationToken ct = default)
        {
            var mission = await db.Missions.AsNoTracking().Include(m => m.Waypoints).SingleAsync(m => m.Id == id, ct);
            var result = new MissionValidationResult { MissionId = id, Stage = stage };
            if (Fail) result.Issues.Add("Fixture preflight blocked");
            db.MissionValidationRuns.Add(new MissionValidationRun { Id = result.Id, MissionId = id, ActorId = actor, Stage = stage,
                IsValid = result.IsValid, ResultStatus = result.Outcome, CheckedAt = result.CheckedAt, PlanHash = MissionValidationService.Hash(mission), ResultJson = JsonSerializer.Serialize(result) });
            await db.SaveChangesAsync(ct);
            return result;
        }
        public Task<MissionValidationResult?> LatestAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MissionValidationResult?> LatestPreflightAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MissionValidationResult?> ReadAsync(Guid id, Guid actor, bool manager, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MissionValidationResult?> ReadLatestRunAsync(Guid id, Guid actor, bool manager, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, MissionValidationReadSnapshot>> ReadManyAsync(IReadOnlyCollection<Mission> missions, Guid actor, bool manager, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MissionValidationResult?> ReadRunAsync(Guid id, Guid run, Guid actor, bool manager, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MissionValidationResult> EvaluateAsync(Mission mission, string stage, CancellationToken ct = default) => throw new NotSupportedException();
        public Task StoreAsync(Mission mission, Guid actor, MissionValidationResult result, bool persisted = true, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SyncPreflightAlertAsync(Guid id, MissionValidationResult result, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
