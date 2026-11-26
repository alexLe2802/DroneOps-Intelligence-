using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Request.Missions;
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
        var saved = await LoadMissionAsync(missionId, userId, isManager, cancellationToken);
        var context = await BuildContextAsync(saved, request.UpdateRequestId, userId, isManager, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entity = new AiAssessment
        {
            Id = Guid.NewGuid(), MissionRef = saved.Id.ToString(), MissionVersion = request.MissionVersion, RequestedBy = userId,
            Provider = "historical-evidence", Model = "normalized-euclidean-v2", ContextHash = HashContext(context.Json),
            ContextSnapshot = context.Json, Status = "Available", Result = JsonSerializer.Serialize(context.Result, JsonOptions),
            CreatedAt = now, CompletedAt = now
        };
        db.AiAssessments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity, request.UpdateRequestId, null);
    }

    private async Task<(string Json, AiAssessmentResultResponse Result)> BuildContextAsync(
        Mission saved, Guid? updateRequestId, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        var mission = saved;
        MissionUpdateRequest? proposal = null;
        if (updateRequestId is { } proposalId)
        {
            proposal = await db.MissionUpdateRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == proposalId && x.MissionId == saved.Id && x.Status == "Pending", cancellationToken)
                ?? throw new KeyNotFoundException("Pending proposal was not found for this mission.");
            if (proposal.BasePlanHash != MissionValidationService.Hash(saved))
                throw new ArgumentException("The pending proposal is based on an earlier mission plan.");
            var dto = JsonSerializer.Deserialize<CreateMissionRequest>(proposal.ProposedPlanJson, MissionValidationService.JsonOptions)
                ?? throw new ArgumentException("The pending proposal is missing its plan.");
            mission = MissionPlans.Propose(saved, dto);
            mission.UAV = await db.UAVs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mission.UAVId, cancellationToken);
        }

        ValidateEssentialPlan(mission);
        var missionId = mission.Id;

        var candidates = await AuthorizedMissions(userId, isManager)
            .Where(x => x.Id != missionId && x.Status == "Completed")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var ids = candidates.Select(x => x.Id).Append(missionId).ToList();
        var telemetry = await db.TelemetryRecords.AsNoTracking().Where(x => x.MissionId.HasValue && ids.Contains(x.MissionId.Value) && x.IsValid).ToListAsync(cancellationToken);
        var executions = await db.Set<MissionFlightExecution>().AsNoTracking()
            .Where(x => ids.Contains(x.MissionId)).ToDictionaryAsync(x => x.MissionId, cancellationToken);
        var incidents = await db.Incidents.AsNoTracking().Where(x => ids.Contains(x.MissionId)).ToListAsync(cancellationToken);

        FlightFeature Feature(Mission value)
        {
            executions.TryGetValue(value.Id, out var execution);
            var uavId = execution?.UavId ?? value.UAVId;
            var points = telemetry.Where(x => x.MissionId == value.Id && x.UAVId == uavId
                && (execution == null || (x.SourceTimestamp >= execution.StartedAt && x.SourceTimestamp <= execution.CompletedAt)))
                .OrderBy(x => x.Id).ToList();
            var eventIds = incidents.Where(x => x.MissionId == value.Id).Select(x => x.Id).Order().ToList();
            double? Average(Func<TelemetryRecord, decimal?> selector)
            {
                var values = points.Select(selector).Where(x => x.HasValue).Select(x => (double)x!.Value).ToList();
                return values.Count == 0 ? null : values.Average();
            }
            var duration = execution != null
                ? (execution.CompletedAt - execution.StartedAt)?.TotalMinutes
                : (value.EndTime - value.StartTime)?.TotalMinutes;
            return new(value.Id, value.Name, Average(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180 ? x.Latitude : null),
                Average(x => x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180 ? x.Longitude : null), duration,
                Average(x => x.Altitude), uavId, eventIds.Count, eventIds);
        }

        var target = new FlightFeature(mission.Id, mission.Name,
            (double)mission.Waypoints.Average(x => x.Latitude), (double)mission.Waypoints.Average(x => x.Longitude),
            (mission.EndTime!.Value - mission.StartTime!.Value).TotalMinutes,
            (double)mission.Waypoints.Average(x => x.Altitude!.Value), mission.UAVId, null, []);
        var ranked = HistoricalSimilarity.Rank(target, candidates.Select(Feature));
        var usedFeatures = new List<string>
            { "centroidLatitude", "centroidLongitude", "durationMinutes", "meanAltitudeMeters" };
        var similar = ranked.Select(x => new SimilarFlightResponse
        {
            MissionId = x.Flight.MissionId, MissionName = x.Flight.MissionName, SimilarityScore = x.Score,
            IncidentIds = x.Flight.IncidentIds.ToList(), Features = FeatureDictionary(x.Flight),
            UsedFeatures = x.Flight.UavId.HasValue ? [.. usedFeatures, "uavMatch"] : usedFeatures
        }).ToList();
        var missing = new List<string>();
        if (similar.Count == 0)
            missing.Add("No authorized completed flights have the minimum GPS, duration and altitude evidence for comparison.");
        var similarIds = similar.Select(x => x.MissionId).ToHashSet();
        var risks = incidents.Where(x => similarIds.Contains(x.MissionId)).GroupBy(x => x.Type).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new AiRiskResponse
        {
            Category = "Historical", Severity = "Moderate", Title = $"Historical pattern: {group.Key}",
            SourceIncidentIds = group.Select(x => x.Id).Order().ToList(),
            Evidence = $"{group.Count()} recorded incident(s) across {group.Select(x => x.MissionId).Distinct().Count()} similar flight(s). "
                + string.Join("; ", group.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(3).Select(x => x.Title)),
            Precaution = "Inspect the linked causes and corrective actions for relevance to this route and UAV. These are historical events, not detections on the current mission."
        }).ToList();
        var now = DateTimeOffset.UtcNow;
        var batteryRecord = telemetry.Where(t => proposal == null && t.MissionId == mission.Id && t.UAVId == mission.UAVId)
            .OrderByDescending(t => t.SourceTimestamp).ThenByDescending(t => t.ReceivedAt).ThenBy(t => t.Id).FirstOrDefault();
        var planHash = MissionValidationService.Hash(mission);
        var latestValidation = await db.MissionValidationRuns.AsNoTracking().Where(r => r.MissionId == mission.Id && r.UpdateRequestId == updateRequestId && r.PlanHash == planHash)
            .OrderByDescending(r => r.CheckedAt).ThenBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
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
            foreach (var point in mission.Waypoints.OrderBy(w => w.SequenceOrder).Where(w => w.Navigation?.EmergencyLandingCandidate == true || w.ActionType == "Land"))
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
            Retrieval = new SimilarityMethodResponse
            {
                FeatureSchema = "planned-vs-recorded-centroid-duration-altitude-v2",
                UsedFeatures = similar.SelectMany(x => x.UsedFeatures).Distinct().ToList(),
                MissingFeatures = ["incidentCount"],
                TargetFeatures = FeatureDictionary(target)
            },
            Summary = $"{risks.Count(x => x.Category != "Historical")} current-plan risk finding(s), {missing.Count} evidence gap(s), and {risks.Count(x => x.Category == "Historical")} historical pattern(s) to review. This is advisory evidence, not clearance to fly.",
            Risks = risks, MissingData = missing, HistoricalFlights = similar,
            Precautions = risks.Count == 0 ? ["Continue mandatory validation and manual operational review."] : ["Open every linked incident before approval.", "Do not treat similarity as a probability of safety."],
            Disclaimer = "Historical similarity is explainable decision support, not a safety probability and not an approval decision."
        };
        var context = new
        {
            schema = 2, managerEvidence = isManager, targetMissionId = mission.Id, updateRequestId, planHash,
            basePlanHash = proposal?.BasePlanHash,
            uav = new { mission.UAVId, mission.UAV?.MaxFlightTimeMinutes, mission.UAV?.BatteryCapacityMah, mission.UAV?.BatteryMaxVoltage },
            validationRunId = validation == null ? (Guid?)null : latestValidation?.Id,
            validationRevision = validation == null ? null : latestValidation?.RestrictionRevision,
            weather,
            batteryRecordId = battery.TelemetrySourceTimestamp.HasValue ? batteryRecord?.Id : null,
            target = FeatureDictionary(target),
            result
        };
        return (JsonSerializer.Serialize(context, JsonOptions), result);
    }

    public async Task<List<AiAssessmentResponse>> GetByMissionAsync(string missionRef, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(missionRef, out var missionId)) throw new ArgumentException("Mission reference must be a UUID.");
        var mission = await LoadMissionAsync(missionId, userId, isManager, cancellationToken);
        var reports = await db.AiAssessments.AsNoTracking().Where(x => x.MissionRef == missionId.ToString()
                && (isManager || x.RequestedBy == userId))
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var contexts = new Dictionary<(Guid? UpdateRequestId, Guid Actor, bool Manager), (string? Hash, string? Reason)>();
        var responses = new List<AiAssessmentResponse>(reports.Count);
        foreach (var report in reports)
        {
            using var snapshot = JsonDocument.Parse(report.ContextSnapshot);
            var root = snapshot.RootElement;
            var updateRequestId = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("updateRequestId", out var id)
                && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var parsedId) ? parsedId : (Guid?)null;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 2)
            {
                responses.Add(Map(report, updateRequestId, "This report predates full context tracking. Run a new assessment."));
                continue;
            }

            var managerEvidence = root.TryGetProperty("managerEvidence", out var scope) && scope.ValueKind == JsonValueKind.True;
            if (managerEvidence && !isManager) continue;
            var actor = managerEvidence ? userId : report.RequestedBy;
            var key = (updateRequestId, actor, managerEvidence);
            if (!contexts.TryGetValue(key, out var current))
            {
                try
                {
                    // Rebuild within the caller's authorized evidence scope.
                    var context = await BuildContextAsync(mission, updateRequestId, actor, managerEvidence, cancellationToken);
                    current = (HashContext(context.Json), null);
                }
                catch (KeyNotFoundException e) { current = (null, e.Message); }
                catch (ArgumentException e) { current = (null, e.Message); }
                contexts.Add(key, current);
            }
            responses.Add(Map(report, updateRequestId, current.Hash == report.ContextHash
                ? null : current.Reason ?? "The plan or assessment evidence has changed since this report was captured."));
        }
        return responses;
    }

    private async Task<Mission> LoadMissionAsync(Guid id, Guid userId, bool isManager, CancellationToken cancellationToken) =>
        await AuthorizedMissions(userId, isManager).Include(x => x.UAV).Include(x => x.Waypoints)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Mission was not found in the authorized scope.");

    private static void ValidateEssentialPlan(Mission mission)
    {
        if (mission.UAV is null)
            throw new ArgumentException("Assign a UAV to the plan before requesting an assessment.");
        if (mission.Waypoints.Count < 2 || mission.Waypoints.Any(x =>
            x.Latitude is < -90 or > 90 || x.Longitude is < -180 or > 180 || !x.Altitude.HasValue))
            throw new ArgumentException("Provide at least two valid route waypoints with altitudes.");
        if (mission.StartTime is not { } start || mission.EndTime is not { } end || start == default || end <= start)
            throw new ArgumentException("Provide a valid mission start and end time.");
        if (mission.AverageSpeedMetersPerSecond is not > 0 || mission.MaxSpeedMetersPerSecond is not > 0
            || mission.MaxSpeedMetersPerSecond < mission.AverageSpeedMetersPerSecond)
            throw new ArgumentException("Provide positive cruise and maximum speeds, with maximum speed at least cruise speed.");
    }

    private static string HashContext(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private IQueryable<Mission> AuthorizedMissions(Guid userId, bool isManager) =>
        isManager ? db.Missions.AsNoTracking() : db.Missions.AsNoTracking().Where(x => x.CreatedBy == userId);

    private static Dictionary<string, double?> FeatureDictionary(FlightFeature value) => new()
    {
        ["centroidLatitude"] = value.Latitude, ["centroidLongitude"] = value.Longitude,
        ["durationMinutes"] = value.DurationMinutes, ["meanAltitudeMeters"] = value.AltitudeMeters,
        ["incidentCount"] = value.IncidentCount
    };

    private static AiAssessmentResponse Map(AiAssessment value, Guid? updateRequestId, string? outdatedReason) => new()
    {
        Id = value.Id, MissionRef = value.MissionRef, MissionVersion = value.MissionVersion,
        UpdateRequestId = updateRequestId, ContextType = updateRequestId.HasValue ? "PendingProposal" : "CurrentMission",
        Provider = value.Provider, Model = value.Model, Status = value.Status,
        ContextHash = value.ContextHash, IsOutdated = outdatedReason != null, OutdatedReason = outdatedReason,
        ErrorCode = value.ErrorCode, CreatedAt = value.CreatedAt, CompletedAt = value.CompletedAt,
        Result = string.IsNullOrWhiteSpace(value.Result) ? null : JsonSerializer.Deserialize<AiAssessmentResultResponse>(value.Result, JsonOptions)
    };
}
