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
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var start = DateTimeOffset.UtcNow.AddMinutes(-2);
        var limitsJson = JsonSerializer.Serialize(new AutomaticFlightLimits());
        await db.Missions.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, start.UtcDateTime)
            .SetProperty(m => m.EndTime, start.AddMinutes(10).UtcDateTime).SetProperty(m => m.Status, "Approved")
            .SetProperty(m => m.AverageSpeedMetersPerSecond, 3m).SetProperty(m => m.MaxSpeedMetersPerSecond, 5m)
            .SetProperty(m => m.AltitudeReference, "Relative").SetProperty(m => m.AutomaticFlightLimitsJson, limitsJson));
        db.Waypoints.AddRange(new Waypoint { Id = Guid.NewGuid(), MissionId = id, SequenceOrder = 1, Latitude = 10.77m, Longitude = 106.69m, Altitude = 20, ActionType = "TakeOff", Navigation = new(PlannedHome: new PlannedHome(10.77m, 106.69m)) },
            new Waypoint { Id = Guid.NewGuid(), MissionId = id, SequenceOrder = 2, Latitude = 10.771m, Longitude = 106.691m, Altitude = 20, ActionType = "Land" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlightExecution:Enabled"] = "true" }).Build();
        var preparation = new MissionAutomationService(db, validation, config);
        var clock = new FixtureTime { Now = DateTimeOffset.UtcNow };
        var controls = new FlightControlService(db, preparation, config, clock);
        var ready = await preparation.ReadAsync(id, actor, default);
        var configured = JsonSerializer.SerializeToElement(await controls.ConfigureAsync(id, actor, ready.PlanHash,
            new MissionCompileOptions("ArduPilot", AcceptanceRadiusMeters: 2, YawPolicy: "Autopilot"), default), Json);
        var hash = configured.GetProperty("compiledHash").GetString()!;
        var evidence = new FlightControllerEvidence(Guid.NewGuid(), "ArduPilot", false, 1, true, true, null, VerifiedEnvelopeHash: hash);
        var upload = JsonSerializer.SerializeToElement(await controls.PollAsync(connection, evidence, null, default), Json).GetProperty("command").GetProperty("id").GetGuid();
        evidence = evidence with { UploadedHash = hash };
        await controls.PollAsync(connection, evidence, new(upload, true, hash), default);
        await preparation.PresenceAsync(id, actor, default);
        await controls.PollAsync(connection, evidence with { PreArmReady = false }, null, default);
        var rejected = false;
        try { await preparation.ConfirmAsync(id, actor, ready.PlanHash, default); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "late agreement rejects failed FC pre-arm even with a verified upload");
        await controls.PollAsync(connection, evidence, null, default);
        await preparation.ConfirmAsync(id, actor, ready.PlanHash, default);
        clock.Now = DateTimeOffset.UtcNow;
        await controls.ReadAsync(id, actor, true, default); await controls.PollAsync(connection, evidence, null, default);
        var arm = JsonSerializer.SerializeToElement(await controls.PollAsync(connection, evidence, null, default), Json);
        var stored = JsonSerializer.Deserialize<FlightControlState>((await db.MissionAutomationStates.AsNoTracking().SingleAsync(s => s.MissionId == id)).ExecutionJson!, Json)!;
        var armId = stored.Commands.Single(c => c.Action == "Arm").Id;
        evidence = evidence with { Armed = true };
        var startResponse = JsonSerializer.SerializeToElement(await controls.PollAsync(connection, evidence, new(armId, true, null), default), Json);
        var startId = startResponse.GetProperty("command").GetProperty("id").GetGuid();
        await controls.PollAsync(connection, evidence, new(startId, true, null), default);
        Check((await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id)).Status == "Approved", "START ACK is not a fabricated takeoff");
        evidence = evidence with { LandedState = 2, MissionMode = true, ReachedCommands = stored.WaypointCommands.Take(1).ToArray() };
        await controls.PollAsync(connection, evidence, null, default);
        Check(await db.MissionFlightExecutions.AnyAsync(e => e.MissionId == id && e.CompletedAt == null), "one drone creates one immutable observed execution");
        var plannedEnd = (await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id)).EndTime;
        evidence = evidence with { LandedState = 1, LandedObservedAtUtc = clock.Now };
        await controls.PollAsync(connection, evidence, null, default);
        clock.Now = clock.Now.AddSeconds(1); evidence = evidence with { LandedObservedAtUtc = clock.Now };
        await controls.PollAsync(connection, evidence, null, default);
        Check(!await db.FlightCompletionEvents.AnyAsync(e => e.MissionId == id) && (await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id)).EndTime == plannedEnd,
            "landing while armed never completes the mission or replaces its end time");
        clock.Now = clock.Now.AddSeconds(1);
        evidence = evidence with { Armed = false, LandedState = 1, MissionCurrent = stored.WaypointCommands.Last(), LandedObservedAtUtc = clock.Now };
        await controls.PollAsync(connection, evidence, null, default);
        clock.Now = clock.Now.AddSeconds(1); evidence = evidence with { LandedObservedAtUtc = clock.Now };
        await controls.PollAsync(connection, evidence, null, default); await controls.PollAsync(connection, evidence, null, default);
        Check(await db.FlightCompletionEvents.CountAsync(e => e.MissionId == id && e.IsTrusted) == 1, "landing and completion replay create one trusted terminal event");
        Check(await db.PostFlightReports.CountAsync(r => r.MissionId == id) == 1, "one completed route queues one post-flight report");
        Check((await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id)).EndTime == clock.Now.UtcDateTime, "mission end uses observed landing and disarm time instead of the booked end");
        Check((await preparation.ReadAsync(id, actor, default)).ActualEndedAt == clock.Now, "preparation API exposes the trusted execution end time");
        var completedExecution = await db.MissionFlightExecutions.AsNoTracking().SingleAsync(e => e.MissionId == id);
        Check(JsonDocument.Parse(completedExecution.ApprovedPlanJson).RootElement.GetProperty("endTime").GetDateTime() == plannedEnd,
            "approved execution snapshot retains the original booked end time");
        Check((await db.Missions.AsNoTracking().SingleAsync(m => m.Id == id)).Status == "Completed", "verified full route and landing complete the mission");
    }
    private sealed class FixtureTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
