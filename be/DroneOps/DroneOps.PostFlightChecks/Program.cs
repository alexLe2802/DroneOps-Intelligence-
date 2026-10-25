using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase($"post-flight-checks-{Guid.NewGuid()}")
    .Options;
await using var db = new AppDbContext(options);
var service = new PostFlightService(new PostFlightRepository(db));
var passed = 0;
var pilotId = Guid.NewGuid();
var managerId = Guid.NewGuid();
var uavId = Guid.NewGuid();
var missionId = Guid.NewGuid();
var noTelemetryMissionId = Guid.NewGuid();
var failedMissionId = Guid.NewGuid();
var overdueActiveMissionId = Guid.NewGuid();
var overdueApprovedMissionId = Guid.NewGuid();
var overdueDraftMissionId = Guid.NewGuid();
var overdueNoTelemetryMissionId = Guid.NewGuid();
var historySyncMissionId = Guid.NewGuid();
var legacyAvailableReportMissionId = Guid.NewGuid();
var now = DateTimeOffset.UtcNow;

db.Users.AddRange(
    new User { Id = pilotId, FullName = "Pilot One", Email = "pilot@example.test", CreatedAt = now },
    new User { Id = managerId, FullName = "Manager", Email = "manager@example.test", CreatedAt = now });
db.UAVs.Add(new UAV { Id = uavId, Code = "UAV-01", Name = "Test UAV", Status = "Active", CreatedAt = now.UtcDateTime });
db.Missions.AddRange(
    Mission(missionId, "Completed route", "Approved"),
    Mission(noTelemetryMissionId, "Missing telemetry", "Approved"),
    Mission(failedMissionId, "Failed flight", "Failed"),
    OverdueMission(overdueActiveMissionId, "Overdue active flight", "Active"),
    OverdueMission(overdueApprovedMissionId, "Overdue approved flight", "Approved"),
    OverdueMission(overdueNoTelemetryMissionId, "Overdue flight without telemetry", "Approved"),
    OverdueMission(overdueDraftMissionId, "Expired draft", "Draft"));
db.TelemetryRecords.AddRange(
    Sample(missionId, now.AddMinutes(-10), armed: true, 80m, 12m, 90),
    Sample(missionId, now, armed: false, 20m, 0m, 60),
    Sample(overdueActiveMissionId, now.AddMinutes(-20), armed: true, 40m, 8m, 90),
    Sample(overdueActiveMissionId, now.AddMinutes(-11), armed: false, 5m, 1m, 70),
    Sample(overdueApprovedMissionId, now.AddMinutes(-20), armed: true, 30m, 7m, 85),
    Sample(overdueApprovedMissionId, now.AddMinutes(-11), armed: false, 3m, 0m, 75));
db.Incidents.AddRange(
    new Incident
    {
        Id = Guid.NewGuid(), MissionId = missionId, Title = "Low battery", Type = "LowBattery",
        Severity = "High", Status = "Resolved", CreatedAt = now.AddMinutes(-2)
    },
    new Incident
    {
        Id = Guid.NewGuid(), MissionId = overdueActiveMissionId, Title = "Route deviation",
        Type = "RouteDeviation", Severity = "Low", Status = "Resolved", CreatedAt = now.AddMinutes(-15)
    });
await db.SaveChangesAsync();

Check((await db.Missions.FindAsync(missionId))!.Status == "Approved",
    "armed-to-disarmed telemetry alone does not complete a flight");

await ExpectThrowsAsync<KeyNotFoundException>(
    () => service.GetReportByMissionIdAsync(missionId, pilotId, false),
    "reading a missing report does not create one");
Check((await db.Missions.FindAsync(missionId))!.Status == "Approved",
    "reading a missing report does not mutate mission status");
await ExpectThrowsAsync<InvalidOperationException>(
    () => service.FinalizeAndGenerateReportAsync(
        missionId, new FinalizeFlightRequest(), managerId, true),
    "manager finalize requires a trusted completion event");
Check((await db.Missions.FindAsync(missionId))!.Status == "Approved",
    "rejected finalize does not mutate mission status");

var queued = await service.RecordTrustedCompletionAsync("completion-1", new RecordFlightCompletionRequest
{
    MissionId = missionId, UavId = uavId, SourceId = "trusted-simulator",
    EventType = "MISSION_COMPLETED", CompletedAt = now
});
Check(queued.GenerationStatus == "Queued", "trusted completion queues report revision 1");
Check((await db.Missions.FindAsync(missionId))!.Status == "Completed", "trusted event completes mission");

var duplicate = await service.RecordTrustedCompletionAsync("completion-1", new RecordFlightCompletionRequest
{
    MissionId = missionId, UavId = uavId, SourceId = "trusted-simulator",
    EventType = "MISSION_COMPLETED", CompletedAt = now
});
Check(duplicate.ReportId == queued.ReportId, "completion ingestion is idempotent");

Check(await service.ProcessQueuedReportsAsync() == 1, "queued report is processed");
var report = await service.GetReportByMissionIdAsync(missionId, pilotId, false);
Check(report.GenerationStatus == "Available" && report.Revision == 1, "report becomes available");
Check(report.TotalTelemetryRecords == 2 && report.TotalIncidents == 1, "report consolidates evidence");
Check(report.TotalFlightDurationMinutes == 10 && report.BatteryConsumedPercentage == 30,
    "report metrics use covered telemetry window");

var generatedAt = report.GeneratedAt!.Value;
var late = Sample(missionId, now.AddMinutes(-5), armed: true, 50m, 5m, 75);
late.ReceivedAt = generatedAt.AddTicks(1);
db.TelemetryRecords.Add(late);
await db.SaveChangesAsync();
await service.ProcessQueuedReportsAsync();
report = await service.GetReportByMissionIdAsync(missionId, pilotId, false);
Check(report.GenerationStatus == "Outdated", "late telemetry marks current report outdated");
await Task.Delay(10);

var regenerated = await service.RegenerateReportAsync(missionId,
    new RegeneratePostFlightReportRequest { Reason = "Include late telemetry" }, managerId);
Check(regenerated.GenerationStatus == "Queued" && regenerated.Revision == 2,
    "manager can queue a new immutable revision");
await service.ProcessQueuedReportsAsync();
var revisions = await service.GetReportRevisionsAsync(missionId, managerId, true);
Check(revisions.Count == 2 && revisions.Single(x => x.Revision == 1).IsCurrent == false &&
      revisions.Single(x => x.Revision == 2).GenerationStatus == "Available",
    "older report revision is preserved");
Check(revisions.Single(x => x.Revision == 2).TotalTelemetryRecords == 3,
    "regenerated report includes late telemetry");

await service.RecordTrustedCompletionAsync("completion-no-data", new RecordFlightCompletionRequest
{
    MissionId = noTelemetryMissionId, UavId = uavId, SourceId = "trusted-simulator",
    EventType = "FLIGHT_COMPLETED", CompletedAt = now
});
await service.ProcessQueuedReportsAsync();
var failedReport = await service.GetReportByMissionIdAsync(noTelemetryMissionId, pilotId, false);
Check(failedReport.GenerationStatus == "Failed" && !string.IsNullOrWhiteSpace(failedReport.FailureReason),
    "generation failure is explicit and flight evidence is preserved");

var analytics = await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest(), pilotId);
Check(analytics.TotalFlights == 7 && analytics.CompletedFlights == 5 &&
      analytics.SuccessRatePercentage == 71.4,
    "completion rate uses all terminal flights instead of report count");
Check(analytics.ReportsAvailable == 4 && analytics.ReportsExcluded == 3 &&
      analytics.ReportCoveragePercentage == 57.1,
    "analytics discloses report coverage and exclusions");
Check(analytics.TotalIncidentsRecorded == 2 && analytics.IncidentSeverityBreakdown["High"] == 1 &&
      analytics.IncidentSeverityBreakdown["Low"] == 1 && analytics.CompletionTrend.Count == 1,
    "analytics includes severity breakdown and daily trend");
Check((await db.Missions.FindAsync(overdueActiveMissionId))!.Status == "Completed" &&
      (await db.Missions.FindAsync(overdueApprovedMissionId))!.Status == "Completed",
    "analytics synchronizes overdue active and approved missions");
Check((await db.Missions.FindAsync(overdueDraftMissionId))!.Status == "Cancelled",
    "analytics cancels overdue draft missions with a valid terminal status");
var incidentReport = await service.GetReportByMissionIdAsync(overdueActiveMissionId, pilotId, false);
var safeReport = await service.GetReportByMissionIdAsync(overdueApprovedMissionId, pilotId, false);
var scheduledReport = await service.GetReportByMissionIdAsync(overdueNoTelemetryMissionId, pilotId, false);
Check(incidentReport.GenerationStatus == "Available" && incidentReport.TotalIncidents == 1 &&
      incidentReport.SafetyEvaluation!.Contains("RISK", StringComparison.OrdinalIgnoreCase),
    "overdue completed flight report records incidents");
Check(safeReport.GenerationStatus == "Available" && safeReport.TotalIncidents == 0 &&
      safeReport.SafetyEvaluation!.Contains("No incidents", StringComparison.OrdinalIgnoreCase),
    "overdue completed flight report records safe completion");
Check(scheduledReport.GenerationStatus == "Available" && scheduledReport.TotalTelemetryRecords == 0 &&
      scheduledReport.TotalFlightDurationMinutes == 10,
    "overdue approved mission without telemetry receives a scheduled completion report");

db.Missions.Add(OverdueMission(historySyncMissionId, "History synchronization", "Active"));
db.TelemetryRecords.AddRange(
    Sample(historySyncMissionId, now.AddMinutes(-20), armed: true, 25m, 5m, 80),
    Sample(historySyncMissionId, now.AddMinutes(-11), armed: false, 2m, 0m, 72));
await db.SaveChangesAsync();
var history = await service.GetFlightHistoryAsync(new GetFlightHistoryRequest
{
    Search = "History synchronization"
}, pilotId);
Check(history.Items.Single().Status == "Completed" &&
      history.Items.Single().ReportStatus == "Available",
    "flight history synchronizes overdue mission and report before returning");

db.Missions.Add(OverdueMission(
    legacyAvailableReportMissionId, "Legacy active mission with available report", " Active "));
db.PostFlightReports.Add(new PostFlightReport
{
    Id = Guid.NewGuid(), MissionId = legacyAvailableReportMissionId, Revision = 1,
    IsCurrent = true, GenerationStatus = "Available", Status = "Completed",
    GeneratedAt = now.AddMinutes(-5), CreatedAt = now.AddMinutes(-10),
    GenerationReason = "Legacy imported report"
});
db.Incidents.AddRange(
    new Incident
    {
        Id = Guid.NewGuid(), MissionId = legacyAvailableReportMissionId, Title = "Legacy incident 1",
        Type = "Safety", Severity = "Medium", Status = "Resolved", CreatedAt = now.AddMinutes(-15)
    },
    new Incident
    {
        Id = Guid.NewGuid(), MissionId = legacyAvailableReportMissionId, Title = "Legacy incident 2",
        Type = "Safety", Severity = "Low", Status = "Resolved", CreatedAt = now.AddMinutes(-14)
    });
await db.SaveChangesAsync();
var legacyHistory = await service.GetFlightHistoryAsync(new GetFlightHistoryRequest
{
    Search = "Legacy active mission"
}, pilotId);
Check(legacyHistory.Items.Single().Status == "Completed" &&
      legacyHistory.Items.Single().ReportStatus == "Available" &&
      legacyHistory.Items.Single().TotalIncidents == 2,
    "legacy overdue Active mission with an available report is synchronized to Completed");
var legacyReport = await service.GetReportByMissionIdAsync(legacyAvailableReportMissionId, pilotId, false);
Check(legacyReport.TotalIncidents == 2 &&
      legacyReport.SafetyEvaluation!.Contains("INCIDENTS", StringComparison.OrdinalIgnoreCase),
    "existing available report is refreshed with overdue mission incident outcome");

var emptyAnalytics = await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest
{
    UavId = Guid.NewGuid()
}, pilotId);
Check(emptyAnalytics.SuccessRatePercentage == null && emptyAnalytics.ReportCoveragePercentage == null,
    "no data is not represented as 100 percent success");

await ExpectThrowsAsync<ArgumentException>(
    () => service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest
    {
        FromDate = now.UtcDateTime, ToDate = now.AddDays(-1).UtcDateTime
    }, pilotId),
    "invalid analytics date range is rejected");

Console.WriteLine($"{passed} post-flight checks passed.");

Mission Mission(Guid id, string name, string status) => new()
{
    Id = id, CreatedBy = pilotId, UAVId = uavId, Name = name, Status = status,
    StartTime = now.AddMinutes(-10).UtcDateTime, EndTime = status == "Failed" ? now.UtcDateTime : null,
    CreatedAt = now.AddHours(-1).UtcDateTime
};

Mission OverdueMission(Guid id, string name, string status) => new()
{
    Id = id, CreatedBy = pilotId, UAVId = uavId, Name = name, Status = status,
    StartTime = now.AddMinutes(-20).UtcDateTime, EndTime = now.AddMinutes(-10).UtcDateTime,
    CreatedAt = now.AddHours(-1).UtcDateTime
};

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}

async Task ExpectThrowsAsync<TException>(Func<Task> action, string name) where TException : Exception
{
    try { await action(); throw new InvalidOperationException($"FAIL: {name}"); }
    catch (TException) { Check(true, name); }
}

TelemetryRecord Sample(Guid mission, DateTimeOffset timestamp, bool armed,
    decimal altitude, decimal speed, int battery) => new()
{
    Id = Guid.NewGuid(), MissionId = mission, UAVId = uavId, SourceId = "post-flight-fixture",
    SourceTimestamp = timestamp, ReceivedAt = timestamp, RecordedAt = timestamp,
    Armed = armed, Latitude = 10.762622m + altitude / 100000m,
    Longitude = 106.660172m + altitude / 100000m, Altitude = altitude,
    Speed = speed, BatteryLevel = battery, IsValid = true
};
