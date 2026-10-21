using DroneOps.Application.Services.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase($"post-flight-checks-{Guid.NewGuid()}")
    .Options;
await using var db = new AppDbContext(options);
var repository = new PostFlightRepository(db);
var service = new PostFlightService(repository);
var passed = 0;

var pilotId = Guid.NewGuid();
var otherPilotId = Guid.NewGuid();
var uavId = Guid.NewGuid();
var missionId = Guid.NewGuid();
var pendingMissionId = Guid.NewGuid();
var now = DateTimeOffset.UtcNow;

db.Users.AddRange(
    new User { Id = pilotId, FullName = "Pilot One", Email = "pilot@example.test", CreatedAt = now },
    new User { Id = otherPilotId, FullName = "Pilot Two", Email = "other@example.test", CreatedAt = now });
db.Missions.AddRange(
    new Mission
    {
        Id = missionId, CreatedBy = pilotId, UAVId = uavId, Name = "Completed route check",
        Status = "Approved", CreatedAt = now.AddHours(-1).UtcDateTime
    },
    new Mission
    {
        Id = pendingMissionId, CreatedBy = pilotId, UAVId = uavId, Name = "Pending route",
        Status = "Pending", CreatedAt = now.UtcDateTime
    });

// Hai điểm mô phỏng một chuyến bay 10 phút: đã armed rồi disarmed khi hạ cánh.
db.TelemetryRecords.AddRange(
    Sample(missionId, uavId, now.AddMinutes(-10), armed: true, 10.762622m, 106.660172m, 80m, 12m, 90),
    Sample(missionId, uavId, now, armed: false, 10.763622m, 106.661172m, 20m, 0m, 60));
db.Incidents.Add(new Incident
{
    Id = Guid.NewGuid(), MissionId = missionId, Title = "Low battery", Type = "LowBattery",
    Severity = "High", Status = "Resolved", CreatedAt = now.AddMinutes(-2)
});
await db.SaveChangesAsync();

await ExpectThrowsAsync<KeyNotFoundException>(
    () => service.GetReportByMissionIdAsync(missionId, pilotId, false),
    "GET report has no write side effect before finalization");
Check((await db.Missions.FindAsync(missionId))!.Status == "Approved", "GET report keeps mission state unchanged");

await ExpectThrowsAsync<InvalidOperationException>(
    () => service.FinalizeAndGenerateReportAsync(pendingMissionId, currentUserId: pilotId),
    "Pending mission cannot be finalized");

await ExpectThrowsAsync<UnauthorizedAccessException>(
    () => service.FinalizeAndGenerateReportAsync(missionId, currentUserId: otherPilotId),
    "Other pilot cannot finalize the mission");

Check(await service.TryAutoFinalizeAsync(missionId), "Armed-to-disarmed transition auto-finalizes the mission");
var report = await service.GetReportByMissionIdAsync(missionId, pilotId, false);
Check(report.Status == "Completed", "Mission status becomes Completed");
Check(report.TotalTelemetryRecords == 2, "Report consolidates valid telemetry");
Check(report.TotalIncidents == 1, "Report consolidates incident history");
Check(report.TotalFlightDurationMinutes == 10, "Duration uses telemetry timestamps");
Check(report.InitialBatteryLevel == 90 && report.FinalBatteryLevel == 60 && report.BatteryConsumedPercentage == 30,
    "Battery consumption is calculated");
Check(report.TotalDistanceMeters > 0, "GPS distance is calculated");
Check(report.SafetyEvaluation?.StartsWith("HIGH RISK") == true, "Safety evaluation reflects severe incidents");

Check(!await service.TryAutoFinalizeAsync(missionId), "Completed mission is not finalized twice");
Check(await db.PostFlightReports.CountAsync(r => r.MissionId == missionId) == 1, "Exactly one report exists per mission");

var playback = await service.GetTelemetryPlaybackAsync(missionId, pilotId, false);
Check(playback.Count == 2 && playback[0].RecordedAt < playback[1].RecordedAt,
    "Telemetry playback is chronological");

var analytics = await service.GetOperationalAnalyticsAsync(pilotId);
Check(analytics.TotalFlights == 1 && analytics.CompletedFlights == 1 && analytics.TotalIncidentsRecorded == 1,
    "Operational analytics aggregates completed reports");

Console.WriteLine($"{passed} post-flight checks passed.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

async Task ExpectThrowsAsync<TException>(Func<Task> action, string name) where TException : Exception
{
    try
    {
        await action();
        throw new InvalidOperationException($"FAIL: {name}");
    }
    catch (TException)
    {
        Check(true, name);
    }
}

static TelemetryRecord Sample(
    Guid missionId, Guid uavId, DateTimeOffset timestamp, bool armed,
    decimal latitude, decimal longitude, decimal altitude, decimal speed, int battery) => new()
{
    Id = Guid.NewGuid(), MissionId = missionId, UAVId = uavId, SourceId = "post-flight-fixture",
    SourceTimestamp = timestamp, ReceivedAt = timestamp, RecordedAt = timestamp,
    Armed = armed, Latitude = latitude, Longitude = longitude, Altitude = altitude,
    Speed = speed, BatteryLevel = battery, IsValid = true
};
