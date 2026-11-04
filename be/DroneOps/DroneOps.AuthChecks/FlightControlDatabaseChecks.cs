using System.Text.Json;
using DroneOps.API;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class FlightControlDatabaseChecks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task RunAsync(AppDbContext db, IMissionValidationService validation, Guid id, Guid actor, Guid connection)
    {
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        async Task Reject(Func<Task> action, string name)
        { try { await action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
        JsonElement Value(object value) => JsonSerializer.SerializeToElement(value, Json);
        async Task<FlightControlState> Stored() => JsonSerializer.Deserialize<FlightControlState>(
            (await db.MissionAutomationStates.AsNoTracking().SingleAsync(s => s.MissionId == id)).ExecutionJson!, Json)!;
        var mission = await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id);
        await db.Missions.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.AverageSpeedMetersPerSecond, 5m)
            .SetProperty(m => m.MaxSpeedMetersPerSecond, 8m).SetProperty(m => m.AltitudeReference, "Relative"));
        db.Waypoints.AddRange(new Waypoint { Id = Guid.NewGuid(), MissionId = id, SequenceOrder = 1, Latitude = 10.77m, Longitude = 106.69m, Altitude = 20, ActionType = "TakeOff" },
            new Waypoint { Id = Guid.NewGuid(), MissionId = id, SequenceOrder = 2, Latitude = 10.771m, Longitude = 106.691m, Altitude = 20, ActionType = "Land" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var preparation = new MissionAutomationService(db, validation);
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlightExecution:Enabled"] = "true" }).Build();
        var clock = new FixtureTime();
        var control = new FlightControlService(db, preparation, enabled, clock);
        var disabled = new FlightControlService(db, preparation, new ConfigurationBuilder().Build());
        var plan = await preparation.ReadAsync(id, actor, default);
        await Reject(() => disabled.ActAsync(id, actor, "Arm", Guid.NewGuid(), "hash", default), "production default blocks physical commands");
        try { await control.ReadAsync(id, Guid.NewGuid(), true, default); throw new Exception("Unauthorized owner accepted."); }
        catch (KeyNotFoundException) { Check(true, "flight controls cannot access another owner's mission"); }
        var options = new MissionCompileOptions("PX4", AcceptanceRadiusMeters: 2, YawPolicy: "Autopilot", LandingGroundAltitudeMeters: 0);
        var configured = Value(await control.ConfigureAsync(id, actor, plan.PlanHash, options, default));
        var hash = configured.GetProperty("compiledHash").GetString()!;
        Check(hash.Length == 64 && !(await preparation.ReadAsync(id, actor, default)).ConfirmationRecorded, "saving FC settings persists hash and withdraws old consent");
        var evidence = new FlightControllerEvidence(Guid.NewGuid(), "PX4", false, 1, true, true, null);
        var upload = Value(await control.PollAsync(connection, evidence, null, default));
        var uploadId = upload.GetProperty("command").GetProperty("id").GetGuid();
        Check(upload.GetProperty("command").GetProperty("action").GetString() == "Upload", "connected ground FC receives mission before pilot confirmation");
        Check(Value(await control.PollAsync(connection, evidence, null, default)).GetProperty("command").ValueKind == JsonValueKind.Null, "delivered upload is not replayed on repeated poll");
        evidence = evidence with { UploadedHash = hash };
        await control.PollAsync(connection, evidence, new(uploadId, true, hash), default);
        Check((await Stored()).UploadVerified && (await Stored()).Stage == "Ready", "upload result persists verified readiness without ARM");
        await validation.RevalidateAsync(id, actor, false, "Preflight");
        await preparation.PresenceAsync(id, actor, default);
        plan = await preparation.ReadAsync(id, actor, default);
        await preparation.ConfirmAsync(id, actor, plan.PlanHash, default);
        await control.ReadAsync(id, actor, true, default);
        var scheduled = mission.StartTime;
        await db.Missions.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(1)));
        await Reject(() => control.ActAsync(id, actor, "Arm", Guid.NewGuid(), hash, default), "ARM is rejected before scheduled StartTime");
        await db.Missions.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, scheduled));
        await preparation.CancelAsync(id, actor, default);
        await preparation.PresenceAsync(id, actor, default);
        await preparation.ConfirmAsync(id, actor, plan.PlanHash, default);
        Check(Value(await control.ReadAsync(id, actor, true, default)).GetProperty("canArm").GetBoolean(), "a fresh explicit confirmation can restore ARM after cancellation while still disarmed");
        var operation = Guid.NewGuid();
        await control.ActAsync(id, actor, "Arm", operation, hash, default);
        await control.ActAsync(id, actor, "Arm", operation, hash, default);
        Check((await Stored()).Commands.Count(c => c.Id == operation) == 1, "idempotent user operation queues only one ARM");
        var arm = Value(await control.PollAsync(connection, evidence, null, default));
        Check(arm.GetProperty("command").GetProperty("action").GetString() == "Arm", "only explicit pilot request emits ARM");
        evidence = evidence with { Armed = true };
        await control.PollAsync(connection, evidence, new(operation, true, null), default);
        var countdown = await Stored();
        Check(countdown.Stage == "Countdown" && countdown.LaunchAt > DateTimeOffset.UtcNow.AddSeconds(9), "FC ARM result starts ten-second server deadline");
        var early = Value(await control.PollAsync(connection, evidence, null, default));
        Check(early.GetProperty("command").ValueKind == JsonValueKind.Null, "server sends no mission-start command early");
        await preparation.CancelAsync(id, actor, default);
        await control.PollAsync(connection, evidence, null, default);
        Check((await Stored()).Stage == "Cancelled", "withdrawing existing confirmation cancels countdown server-side");
        await preparation.PresenceAsync(id, actor, default);
        await control.PollAsync(connection, evidence, null, default);
        Check((await Stored()).Stage == "Cancelled", "online reconnect does not resume cancelled takeoff");
        await Reject(() => control.ActAsync(id, actor, "Arm", Guid.NewGuid(), hash, default), "already armed aircraft cannot receive a new ARM countdown");
        await control.PollAsync(connection, evidence with { LandedState = 2 }, null, default);
        await Reject(() => control.ActAsync(id, actor, "Disarm", Guid.NewGuid(), hash, default), "in-air DISARM rejected on server");
        await control.PollAsync(connection, evidence, null, default);
        var disarm = Guid.NewGuid();
        await control.ActAsync(id, actor, "Disarm", disarm, hash, default);
        Check((await Stored()).Pending?.Action == "Disarm", "pilot can request ground DISARM after withdrawing consent");
        await control.PollAsync(connection, evidence, null, default);
        await control.PollAsync(connection, evidence with { Armed = false }, new(disarm, true, null), default);
        Check((await Stored()).Stage == "Ready", "observed FC DISARM completes user command");
        evidence = evidence with { Armed = false };
        await control.ReadAsync(id, actor, true, default);
        await preparation.PresenceAsync(id, actor, default);
        await preparation.ConfirmAsync(id, actor, plan.PlanHash, default);
        var secondArm = Guid.NewGuid();
        await control.ActAsync(id, actor, "Arm", secondArm, hash, default);
        await control.PollAsync(connection, evidence, null, default);
        evidence = evidence with { Armed = true };
        await control.PollAsync(connection, evidence, new(secondArm, true, null), default);
        JsonElement after = default;
        for (var second = 1; second <= 10; second++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            after = Value(await control.PollAsync(connection, evidence, null, default));
            await control.ReadAsync(id, actor, true, default);
            if (second < 10) Check(after.GetProperty("command").ValueKind == JsonValueKind.Null, $"no start at countdown second {second}");
        }
        var start = after.GetProperty("command");
        Check(start.GetProperty("action").GetString() == "Start", "exact ten-second deadline emits a single mission-start command");
        await Reject(() => control.ActAsync(id, actor, "Disarm", Guid.NewGuid(), hash, default), "late ground packet cannot allow disarm once mission start was sent");
        await control.PollAsync(connection, evidence, new(start.GetProperty("id").GetGuid(), true, null), default);
        Check((await Stored()).Stage == "Running", "start acknowledgement plus observed armed FC enters Running");
        Check(Value(await control.PollAsync(connection, evidence, null, default)).GetProperty("command").ValueKind == JsonValueKind.Null, "successful start is never resent on later polls");
    }

    private sealed class FixtureTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
