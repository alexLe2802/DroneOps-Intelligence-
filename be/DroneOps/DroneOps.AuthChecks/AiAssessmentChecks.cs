using System.Text.Json;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Services.AI;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

internal static class AiAssessmentChecks
{
    public static async Task RunAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid();
        var aircraft = new UAV { Id = Guid.NewGuid(), MaxFlightTimeMinutes = 30, BatteryCapacityMah = 5200 };
        var mission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, UAV = aircraft, UAVId = aircraft.Id,
            AverageSpeedMetersPerSecond = 5, Waypoints = [
                new() { Id = Guid.NewGuid(), SequenceOrder = 1, Latitude = 10, Longitude = 106 },
                new() { Id = Guid.NewGuid(), SequenceOrder = 2, Latitude = 10.001m, Longitude = 106 }] };
        var previous = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, Status = "Completed", UAVId = aircraft.Id };
        var foreign = new Mission { Id = Guid.NewGuid(), CreatedBy = Guid.NewGuid(), Status = "Completed" };
        var incident = new Incident { Id = Guid.NewGuid(), MissionId = previous.Id, Type = "CommunicationLoss", Title = "Radio link lost" };
        db.AddRange(aircraft, mission, previous, foreign, incident,
            new Incident { Id = Guid.NewGuid(), MissionId = foreign.Id, Type = "LowBattery", Title = "Private evidence" });
        await db.SaveChangesAsync();
        var service = new AiAssessmentService(db);
        var request = new CreateAiAssessmentRequest { MissionRef = mission.Id.ToString(), MissionVersion = 1 };
        var result = (await service.CreateAsync(request, owner, false, default)).Result!;
        Check(result.Battery!.RequiredBatteryPercent > 20 && result.Battery.CurrentBatteryPercent == null,
            "assessment calculates departure requirement before the UAV connects");
        Check(result.Risks.Any(x => x.Category == "Historical" && x.Title.Contains("CommunicationLoss") && x.SourceIncidentIds.Contains(incident.Id)),
            "historical risk identifies the actual incident type and source evidence");
        Check(result.HistoricalFlights.All(x => x.MissionId != foreign.Id) && result.Risks.All(x => !x.Evidence.Contains("Private evidence")),
            "historical incident details remain inside authorized mission scope");
        Check(result.MissingData.Any(x => x.Contains("validation")) && result.MissingData.Any(x => x.Contains("weather")),
            "missing validation and forecast are explicit evidence gaps");
        try
        {
            await service.CreateAsync(request, Guid.NewGuid(), false, default);
            throw new InvalidOperationException("Foreign operator assessment was permitted");
        }
        catch (KeyNotFoundException) { Check(true, "foreign operators cannot assess this mission"); }
        var now = DateTimeOffset.UtcNow;
        db.MissionValidationRuns.Add(new MissionValidationRun { Id = Guid.NewGuid(), MissionId = mission.Id,
            PlanHash = MissionValidationService.Hash(mission), CheckedAt = now,
            ResultJson = JsonSerializer.Serialize(new MissionValidationResult { Issues = ["Restricted route segment"],
                Weather = new WeatherAdvisory { Available = true, Status = "Available", OverallRisk = "High", ThunderstormForecast = true,
                    GeneratedAt = now, ExpiresAt = now.AddMinutes(10), Warnings = ["Thunderstorm on route"] } }) });
        await db.SaveChangesAsync();
        result = (await service.CreateAsync(request, owner, false, default)).Result!;
        Check(result.Risks.Any(x => x.Title == "Mission validation finding" && x.Evidence.Contains("Restricted route segment"))
            && result.Risks.Any(x => x.Title == "Adverse route weather" && x.Severity == "High"),
            "matching fresh validation exposes route and weather risks with evidence");
        var snapshot = result.Battery!.RequiredBatteryPercent;
        aircraft.MaxFlightTimeMinutes = 1;
        mission.Waypoints.Last().Latitude = 12;
        await db.SaveChangesAsync();
        result = (await service.CreateAsync(request, owner, false, default)).Result!;
        Check(result.Risks.Any(x => x.Title == "Mission exceeds one full battery") && result.Battery!.RequiredBatteryPercent > 100,
            "impossible mission demand is a high risk even without telemetry");
        var history = await service.GetByMissionAsync(mission.Id.ToString(), owner, false, default);
        Check(history.Any(x => x.IsOutdated && x.Result?.Battery?.RequiredBatteryPercent == snapshot),
            "plan edits mark stored assessments outdated without rewriting their captured battery evidence");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
