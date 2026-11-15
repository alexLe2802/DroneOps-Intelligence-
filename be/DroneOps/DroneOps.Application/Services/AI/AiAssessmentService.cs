using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.AI;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Application.Services.Missions;

namespace DroneOps.Application.Services.AI;

public sealed class AiAssessmentService(AppDbContext db) : IAiAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiAssessmentResponse> CreateAsync(CreateAiAssessmentRequest request, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.MissionRef, out var missionId)) throw new ArgumentException("Mission reference must be a UUID.");
        if (request.MissionVersion < 1) throw new ArgumentException("Mission version must be positive.");
        var mission = await AuthorizedMissions(userId, isManager).Include(x => x.UAV).Include(x => x.Waypoints)
            .SingleOrDefaultAsync(x => x.Id == missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Mission was not found in the authorized scope.");

        var candidates = await AuthorizedMissions(userId, isManager)
            .Where(x => x.Id != missionId && x.Status == "Completed")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var ids = candidates.Select(x => x.Id).Append(missionId).ToList();
        var telemetry = await db.TelemetryRecords.AsNoTracking().Where(x => x.MissionId.HasValue && ids.Contains(x.MissionId.Value) && x.IsValid).ToListAsync(cancellationToken);
        var incidents = await db.Incidents.AsNoTracking().Where(x => ids.Contains(x.MissionId)).ToListAsync(cancellationToken);

        FlightFeature Feature(Mission value)
        {
            var points = telemetry.Where(x => x.MissionId == value.Id).ToList();
            var eventIds = incidents.Where(x => x.MissionId == value.Id).Select(x => x.Id).Order().ToList();
            double? Average(Func<TelemetryRecord, decimal?> selector)
            {
                var values = points.Select(selector).Where(x => x.HasValue).Select(x => (double)x!.Value).ToList();
                return values.Count == 0 ? null : values.Average();
            }
            var duration = value.StartTime.HasValue && value.EndTime.HasValue ? (value.EndTime.Value - value.StartTime.Value).TotalMinutes : (double?)null;
            return new(value.Id, value.Name, Average(x => x.Latitude), Average(x => x.Longitude), duration,
                Average(x => x.Altitude), value.UAVId, eventIds.Count, eventIds);
        }

        var target = Feature(mission);
        var ranked = HistoricalSimilarity.Rank(target, candidates.Select(Feature));
        var similar = ranked.Select(x => new SimilarFlightResponse
        {
            MissionId = x.Flight.MissionId, MissionName = x.Flight.MissionName, SimilarityScore = x.Score,
            IncidentIds = x.Flight.IncidentIds.ToList(), Features = FeatureDictionary(x.Flight)
        }).ToList();
        var missing = new List<string>();
        if (target.Latitude is null || target.Longitude is null) missing.Add("Target mission has no valid GPS centroid.");
        if (target.AltitudeMeters is null) missing.Add("Target mission has no valid recorded altitude.");
        if (target.DurationMinutes is null) missing.Add("Target mission has no trusted start/end duration.");
        if (similar.Count == 0) missing.Add("No similar completed flights are available in the authorized scope.");
        var similarIds = similar.Select(x => x.MissionId).ToHashSet();
        var risks = incidents.Where(x => similarIds.Contains(x.MissionId)).GroupBy(x => x.Type).Select(group => new AiRiskResponse
        {
            Category = "Historical", Severity = "Moderate", Title = $"Historical pattern: {group.Key}",
            SourceIncidentIds = group.Select(x => x.Id).Order().ToList(),
            Evidence = $"{group.Count()} recorded incident(s) across {group.Select(x => x.MissionId).Distinct().Count()} similar flight(s). "
                + string.Join("; ", group.OrderByDescending(x => x.CreatedAt).Take(3).Select(x => x.Title)),
            Precaution = "Inspect the linked causes and corrective actions for relevance to this route and UAV. These are historical events, not detections on the current mission."
        }).ToList();
        var now = DateTimeOffset.UtcNow;
        var batteryRecord = telemetry.Where(t => t.MissionId == mission.Id && t.UAVId == mission.UAVId)
            .OrderByDescending(t => t.SourceTimestamp).ThenByDescending(t => t.ReceivedAt).FirstOrDefault();
        var planHash = MissionValidationService.Hash(mission);
        var latestValidation = await db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == mission.Id && r.PlanHash == planHash)
            .OrderByDescending(r => r.CheckedAt).FirstOrDefaultAsync(cancellationToken);
        WeatherAdvisory? weather = null;
        MissionValidationResult? validation = null;
        if (latestValidation != null && latestValidation.CheckedAt >= now.AddMinutes(-10) && latestValidation.CheckedAt <= now)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<MissionValidationResult>(latestValidation.ResultJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                validation = snapshot;
                if (snapshot?.Weather is { Available: true } forecast && forecast.ExpiresAt > now && forecast.GeneratedAt <= now)
                    weather = forecast;
            }
            catch (JsonException) { /* Missing evidence is explicit; no synthetic weather. */ }
        }
        var battery = BatteryAssessmentCalculator.Calculate(mission, mission.UAV, batteryRecord, weather, now);
        if (validation is null || validation.Stale || validation.Incomplete || validation.Error)
            missing.Add("Current complete route validation is missing. Run mission validation to check airspace, weather and route restrictions.");
        if (validation is not null)
            foreach (var issue in validation.Issues)
                risks.Add(new AiRiskResponse { Severity = "High", Title = "Mission validation finding", Evidence = issue,
                    Precaution = "Correct the finding and run mission validation again before requesting approval." });
        if (weather is null)
            missing.Add("Fresh weather for this plan is missing. Run mission validation to obtain the route forecast.");
        else if (weather.OverallRisk is "High" or "Critical" || weather.ThunderstormForecast)
            risks.Add(new AiRiskResponse { Severity = "High", Title = "Adverse route weather",
                Evidence = $"Forecast risk: {weather.OverallRisk}; wind {weather.MaxWindMetersPerSecond} m/s; gust {weather.MaxGustMetersPerSecond} m/s; thunderstorm forecast: {weather.ThunderstormForecast}. " + string.Join(" ", weather.Warnings),
                Precaution = "Review the forecast by waypoint; postpone or revise the mission and validate again." });
        var envelopeErrors = AutomaticFlightPolicy.Errors(mission.AutomaticFlightRequested, mission.AltitudeReference,
            mission.AverageSpeedMetersPerSecond ?? 0, mission.MaxSpeedMetersPerSecond ?? 0, mission.AutomaticFlightLimits,
            mission.Waypoints.Select(w => (w.Altitude, w.ActionType)));
        foreach (var error in envelopeErrors)
            risks.Add(new AiRiskResponse { Severity = "High", Title = "Automatic flight envelope requires correction", Evidence = error,
                Precaution = "Correct the mission and obtain fresh validation and manager approval. AI cannot override an operating limit." });
        if (mission.AutomaticFlightRequested && mission.AutomaticFlightLimits is { } limits)
        {
            missing.Add(limits.RecoveryEnergyProfile == null
                ? "Measured recovery-energy data is missing; the endurance heuristic cannot authorize Home/alternate routing."
                : "Recovery measurements are pilot-declared and require actual battery/configuration/payload/condition matching and independent calibration verification before recovery authorization.");
            foreach (var point in mission.Waypoints.Where(w => w.Navigation?.EmergencyLandingCandidate == true || w.ActionType == "Land"))
                if (point.Navigation?.LandingSiteSurvey is not { GroundClear: true, ApproachClear: true } survey
                    || survey.SurveyedAtUtc > now || survey.ValidUntilUtc <= now)
                    missing.Add($"Waypoint {point.SequenceOrder} has no current declared ground/approach inspection. Coordinates alone do not establish landing clearance.");
            if (weather?.Status != "Available" || weather.ExpiresAt <= now || weather.MaxWindMetersPerSecond == null || weather.MaxGustMetersPerSecond == null)
                missing.Add("Fresh route wind and gust evidence is required for the automatic operating envelope.");
            else if (weather.MaxWindMetersPerSecond > limits.MaxWindMetersPerSecond || weather.MaxGustMetersPerSecond > limits.MaxGustMetersPerSecond)
                risks.Add(new AiRiskResponse { Severity = "High", Title = "Wind exceeds the approved automatic envelope",
                    Evidence = $"Forecast wind {weather.MaxWindMetersPerSecond} m/s; gust {weather.MaxGustMetersPerSecond} m/s. Limits: {limits.MaxWindMetersPerSecond}/{limits.MaxGustMetersPerSecond} m/s.",
                    Precaution = "Postpone or amend the mission and obtain fresh validation and approval." });
        }
        missing.AddRange(battery.MissingData);
        if (battery.RequiredBatteryPercent > 100)
            risks.Add(new AiRiskResponse { Severity = "High", Title = "Mission exceeds one full battery",
                Evidence = $"Estimated requirement including reserve: {battery.RequiredBatteryPercent:0.##}%, exceeding 100% even with a full pack.",
                Precaution = "Shorten or split the route, review the UAV endurance specification and recalculate. Charging alone cannot satisfy this estimate." });
        else if (battery.MeetsEstimatedRequirement == false)
            risks.Add(new AiRiskResponse { Severity = "High", Title = "Battery below estimated mission requirement",
                Evidence = $"Fresh battery: {battery.CurrentBatteryPercent}%; estimated requirement including reserve: {battery.RequiredBatteryPercent:0.##}%.",
                Precaution = "Charge or replace the battery and regenerate the assessment before departure." });
        var result = new AiAssessmentResultResponse
        {
            AutomaticFlightLimits = mission.AutomaticFlightLimits, CruiseSpeedMetersPerSecond = mission.AverageSpeedMetersPerSecond, MaxSpeedMetersPerSecond = mission.MaxSpeedMetersPerSecond,
            RecoveryPolicy = mission.AutomaticFlightRequested ? AutomaticFailsafePolicy.Contract(mission.MaxSpeedMetersPerSecond ?? 0) : null,
            Battery = battery,
            Summary = $"{risks.Count(x => x.Category != "Historical")} current-plan risk finding(s), {missing.Count} evidence gap(s), and {risks.Count(x => x.Category == "Historical")} historical pattern(s) to review. This is advisory evidence, not clearance to fly.",
            Risks = risks, MissingData = missing, HistoricalFlights = similar,
            Precautions = risks.Count == 0 ? ["Continue mandatory validation and manual operational review."] : ["Open every linked incident before approval.", "Do not treat similarity as a probability of safety."],
            Disclaimer = "Historical similarity is explainable decision support, not a safety probability and not an approval decision."
        };
        var context = new { targetMissionId = mission.Id, planHash, automaticFlightLimits = mission.AutomaticFlightLimits,
            recoveryPolicy = result.RecoveryPolicy, waypointNavigation = mission.Waypoints.OrderBy(w => w.SequenceOrder).Select(w => new { w.SequenceOrder, w.Latitude, w.Longitude, w.Altitude, w.ActionType, w.Navigation }),
            cruiseSpeed = mission.AverageSpeedMetersPerSecond, maximumSpeed = mission.MaxSpeedMetersPerSecond,
            batteryMaxVoltage = mission.UAV?.BatteryMaxVoltage, battery, target = FeatureDictionary(target), retrieval = result.Retrieval, sourceMissionIds = similar.Select(x => x.MissionId), sourceIncidentIds = similar.SelectMany(x => x.IncidentIds) };
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var entity = new AiAssessment
        {
            Id = Guid.NewGuid(), MissionRef = mission.Id.ToString(), MissionVersion = request.MissionVersion, RequestedBy = userId,
            Provider = "historical-evidence", Model = "normalized-euclidean-v1", ContextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contextJson))).ToLowerInvariant(),
            ContextSnapshot = contextJson, Status = "Available", Result = JsonSerializer.Serialize(result, JsonOptions), CreatedAt = now, CompletedAt = now
        };
        db.AiAssessments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity, planHash);
    }

    public async Task<List<AiAssessmentResponse>> GetByMissionAsync(string missionRef, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(missionRef, out var missionId)) throw new ArgumentException("Mission reference must be a UUID.");
        var mission = await AuthorizedMissions(userId, isManager).Include(x => x.Waypoints)
            .SingleOrDefaultAsync(x => x.Id == missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Mission was not found in the authorized scope.");
        var currentPlanHash = MissionValidationService.Hash(mission);
        return (await db.AiAssessments.AsNoTracking().Where(x => x.MissionRef == missionId.ToString())
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken))
            .Select(value => Map(value, currentPlanHash)).ToList();
    }

    private IQueryable<Mission> AuthorizedMissions(Guid userId, bool isManager) =>
        isManager ? db.Missions.AsNoTracking() : db.Missions.AsNoTracking().Where(x => x.CreatedBy == userId);

    private static Dictionary<string, double?> FeatureDictionary(FlightFeature value) => new()
    {
        ["centroidLatitude"] = value.Latitude, ["centroidLongitude"] = value.Longitude,
        ["durationMinutes"] = value.DurationMinutes, ["meanAltitudeMeters"] = value.AltitudeMeters,
        ["incidentCount"] = value.IncidentCount
    };

    private static AiAssessmentResponse Map(AiAssessment value, string currentPlanHash)
    {
        var isOutdated = !ContextContainsPlanHash(value.ContextSnapshot, currentPlanHash);
        return new()
        {
            Id = value.Id, MissionRef = value.MissionRef, MissionVersion = value.MissionVersion,
            Provider = value.Provider, Model = value.Model, Status = value.Status,
            ContextHash = value.ContextHash, IsOutdated = isOutdated,
            OutdatedReason = isOutdated
                ? "The mission plan has changed since this assessment was captured."
                : null,
            ErrorCode = value.ErrorCode, CreatedAt = value.CreatedAt, CompletedAt = value.CompletedAt,
            Result = string.IsNullOrWhiteSpace(value.Result)
                ? null
                : JsonSerializer.Deserialize<AiAssessmentResultResponse>(value.Result, JsonOptions)
        };
    }

    private static bool ContextContainsPlanHash(string contextSnapshot, string currentPlanHash)
    {
        try
        {
            using var document = JsonDocument.Parse(contextSnapshot);
            return document.RootElement.TryGetProperty("planHash", out var planHash) &&
                   string.Equals(planHash.GetString(), currentPlanHash, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
