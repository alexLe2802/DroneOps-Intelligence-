using System.Text.Json;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Services.AI;
using DroneOps.Application.Services.Missions;
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
        var now = DateTimeOffset.UtcNow;
        var mission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, UAV = aircraft, UAVId = aircraft.Id,
            AverageSpeedMetersPerSecond = 5, MaxSpeedMetersPerSecond = 10,
            StartTime = now.UtcDateTime.AddMinutes(5), EndTime = now.UtcDateTime.AddMinutes(20), Waypoints = [
                new() { Id = Guid.NewGuid(), SequenceOrder = 1, Latitude = 10, Longitude = 106, Altitude = 30 },
                new() { Id = Guid.NewGuid(), SequenceOrder = 2, Latitude = 10.001m, Longitude = 106, Altitude = 30 }] };
        var previous = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, Status = "Completed", UAVId = aircraft.Id, StartTime = now.UtcDateTime.AddMinutes(-20), EndTime = now.UtcDateTime.AddMinutes(-5) };
        var foreign = new Mission { Id = Guid.NewGuid(), CreatedBy = Guid.NewGuid(), Status = "Completed" };
        var incident = new Incident { Id = Guid.NewGuid(), MissionId = previous.Id, Type = "CommunicationLoss", Title = "Radio link lost" };
        db.AddRange(aircraft, mission, previous, foreign, incident,
            new Incident { Id = Guid.NewGuid(), MissionId = foreign.Id, Type = "LowBattery", Title = "Private evidence" });
        db.TelemetryRecords.Add(new TelemetryRecord { Id = Guid.NewGuid(), MissionId = previous.Id, UAVId = aircraft.Id,
            Latitude = 10, Longitude = 106, Altitude = 30, SourceTimestamp = now.AddMinutes(-10) });
        var evidencePoor = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, Status = "Completed", UAVId = aircraft.Id };
        db.Missions.Add(evidencePoor);
        await db.SaveChangesAsync();
        var service = new AiAssessmentService(db);
        var request = new CreateAiAssessmentRequest { MissionRef = mission.Id.ToString(), MissionVersion = 1 };
        var captured = await service.CreateAsync(request, owner, false, default);
        var result = captured.Result!;
        Check(result.HistoricalFlights.Count == 1 && result.HistoricalFlights[0].MissionId == previous.Id,
            "completed missions without minimum route, duration and altitude evidence are excluded");
        Check(!(await service.GetByMissionAsync(request.MissionRef, owner, false, default)).Single().IsOutdated,
            "identical context keeps a report current");
        Check(!(await service.GetByMissionAsync(request.MissionRef, Guid.NewGuid(), true, default)).Single().IsOutdated,
            "manager checks operator reports against the original operator evidence scope");
        var proposalPlan = MissionPlans.Dto(mission);
        proposalPlan.Waypoints[0].Altitude = 60;
        var pending = new MissionUpdateRequest { Id = Guid.NewGuid(), MissionId = mission.Id, ActorId = owner,
            BasePlanHash = MissionValidationService.Hash(mission), ProposedPlanJson = JsonSerializer.Serialize(proposalPlan) };
        db.MissionUpdateRequests.Add(pending);
        await db.SaveChangesAsync();
        var proposalRequest = new CreateAiAssessmentRequest { MissionRef = request.MissionRef, UpdateRequestId = pending.Id };
        var proposed = await service.CreateAsync(proposalRequest, owner, false, default);
        Check(proposed.ContextType == "PendingProposal" && proposed.UpdateRequestId == pending.Id
            && proposed.Result!.Retrieval.TargetFeatures["meanAltitudeMeters"] == 45 && mission.Waypoints.First().Altitude == 30,
            "proposal assessment captures proposed inputs while preserving the saved plan");
        try
        {
            await service.CreateAsync(new CreateAiAssessmentRequest { MissionRef = previous.Id.ToString(), UpdateRequestId = pending.Id },
                owner, false, default);
            throw new InvalidOperationException("Proposal from another mission was accepted");
        }
        catch (KeyNotFoundException) { Check(true, "proposal must belong to the requested mission"); }
        pending.Status = "Rejected";
        await db.SaveChangesAsync();
        Check((await service.GetByMissionAsync(request.MissionRef, owner, false, default)).Single(x => x.Id == proposed.Id).IsOutdated,
            "a proposal report becomes outdated after the proposal leaves Pending");
        Check(await db.AiAssessments.CountAsync() == 2, "history comparison does not create new assessments");
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
        Check((await service.GetByMissionAsync(request.MissionRef, owner, false, default)).Single(x => x.Id == captured.Id).IsOutdated,
            "new validation and weather invalidate history with an unchanged plan");
        var withValidation = await service.CreateAsync(request, owner, false, default);
        incident.Title = "Radio link lost after takeoff";
        await db.SaveChangesAsync();
        Check((await service.GetByMissionAsync(request.MissionRef, owner, false, default)).Single(x => x.Id == withValidation.Id).IsOutdated,
            "changed incident evidence invalidates history with an unchanged plan and validation");
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
        mission.Waypoints.First().Altitude = null;
        await db.SaveChangesAsync();
        var count = await db.AiAssessments.CountAsync();
        try
        {
            await service.CreateAsync(request, owner, false, default);
            throw new InvalidOperationException("Missing essential altitude was accepted");
        }
        catch (ArgumentException) { Check(await db.AiAssessments.CountAsync() == count, "incomplete plan is rejected without persisting a report"); }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
